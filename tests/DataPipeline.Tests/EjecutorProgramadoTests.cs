using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorProgramadoTests
{
    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejmeta_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static string NuevaData() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"ejdata_{Guid.NewGuid():N}.db")};Pooling=False";

    [Fact]
    public async Task Procesa_un_vencido_y_avanza_la_proxima_corrida()
    {
        using var meta = NuevoMeta();
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var ahora = new DateTime(2026, 6, 20, 9, 0, 0);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
            ProximaCorrida = ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Reemplazar, FilasPorLote = 10,
            ColumnasClave = { "id" },
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var source = new FakeSource(new[] { "id" },
            new[] { new Record { Values = new Dictionary<string, object?> { ["id"] = 1 } } }, 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - EjecutorProgramado recibe IEnvioStore (prende Pendiente).
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc, hydrator, new EfEnvioStore(meta));

        var n = await ejecutor.ProcesarVencidosAsync(ahora, CancellationToken.None);

        Assert.Equal(1, n);
        var recargado = await store.GetByIdAsync(def.Id, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 6, 21, 8, 0, 0), recargado!.ProximaCorrida);
        var corridas = await hist.ObtenerRecientesAsync(10, CancellationToken.None);
        Assert.Single(corridas);
        Assert.Equal(EstadoCorrida.Ok, corridas[0].Estado);
    }

    [Fact]
    public async Task Persiste_el_watermark_tras_corrida_exitosa()
    {
        using var meta = NuevoMeta();
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var ahora = new DateTime(2026, 6, 20, 9, 0, 0);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
            ProximaCorrida = ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Agregar, FilasPorLote = 10,
            ColumnaWatermark = "fecha",
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var source = new FakeSource(new[] { "id", "fecha" },
            new[]
            {
                new Record { Values = new Dictionary<string, object?> { ["id"] = 1, ["fecha"] = new DateTime(2026, 6, 20, 18, 0, 0) } },
                new Record { Values = new Dictionary<string, object?> { ["id"] = 2, ["fecha"] = new DateTime(2026, 6, 21, 10, 30, 0) } },
            }, 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - EjecutorProgramado recibe IEnvioStore (prende Pendiente).
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc, hydrator, new EfEnvioStore(meta));

        await ejecutor.ProcesarVencidosAsync(ahora, CancellationToken.None);

        var recargado = await store.GetByIdAsync(def.Id, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 6, 21, 10, 30, 0).ToString("O"), recargado!.UltimoWatermark);
    }

    [Fact]
    public async Task No_persiste_el_watermark_si_la_corrida_fallo()
    {
        using var meta = NuevoMeta();
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var ahora = new DateTime(2026, 6, 20, 9, 0, 0);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
            ProximaCorrida = ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Agregar, FilasPorLote = 10,
            ColumnaWatermark = "fecha",
            // Sin Origen -> PipelineRunner falla controladamente (Success = false).
            Pasos = { }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var source = new FakeSource(new[] { "id", "fecha" }, Array.Empty<Record>(), 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - EjecutorProgramado recibe IEnvioStore (prende Pendiente).
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc, hydrator, new EfEnvioStore(meta));

        await ejecutor.ProcesarVencidosAsync(ahora, CancellationToken.None);

        var recargado = await store.GetByIdAsync(def.Id, CancellationToken.None);
        Assert.Null(recargado!.UltimoWatermark);
    }
}
