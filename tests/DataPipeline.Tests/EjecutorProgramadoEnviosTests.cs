// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el pipeline solo prende la
// bandera. Que la corrida fallida NO la prenda es la garantia de que nunca se empuja una tanda a
// medias.
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Envios;
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorProgramadoEnviosTests
{
    private static readonly DateTime Ahora = new(2026, 9, 12, 3, 0, 0);

    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejpenv_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static string NuevaData() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"ejpenvd_{Guid.NewGuid():N}.db")};Pooling=False";

    private static async Task<(EjecutorProgramado Ej, EfEnvioStore Envios, Guid EnvioId)> ArmarAsync(
        MetadataDbContext meta, bool origenFalla)
    {
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var envios = new EfEnvioStore(meta);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "03:00",
            ProximaCorrida = Ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Reemplazar,
            FilasPorLote = 10, ColumnasClave = { "id" },
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var envio = new Envio
        {
            Id = Guid.NewGuid(), Nombre = "e", Habilitado = true, Pendiente = false,
            PipelineId = def.Id, DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
        };
        await envios.UpsertAsync(envio, CancellationToken.None);

        var source = new FakeSource(new[] { "id" },
            new[] { new Record { Values = new Dictionary<string, object?> { ["id"] = 1 } } }, 10);
        if (origenFalla) source.FallaAlLeer = new InvalidOperationException("origen caido");

        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        var ej = new EjecutorProgramado(store, hist, runner, enc, hydrator, envios);

        return (ej, envios, envio.Id);
    }

    [Fact]
    public async Task Una_corrida_exitosa_prende_la_bandera_de_sus_envios()
    {
        using var meta = NuevoMeta();
        var (ej, envios, envioId) = await ArmarAsync(meta, origenFalla: false);

        await ej.ProcesarVencidosAsync(Ahora, CancellationToken.None);

        Assert.True((await envios.GetByIdAsync(envioId, CancellationToken.None))!.Pendiente);
    }

    [Fact]
    public async Task Una_corrida_fallida_no_prende_nada()
    {
        using var meta = NuevoMeta();
        var (ej, envios, envioId) = await ArmarAsync(meta, origenFalla: true);

        await ej.ProcesarVencidosAsync(Ahora, CancellationToken.None);

        Assert.False((await envios.GetByIdAsync(envioId, CancellationToken.None))!.Pendiente);
    }
}
