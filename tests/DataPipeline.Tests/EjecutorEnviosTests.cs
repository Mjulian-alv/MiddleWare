// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de envios.
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Envios;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorEnviosTests
{
    private static readonly DateTime Ahora = new(2026, 9, 12, 3, 4, 0);
    private static readonly string[] Clave = { "id" };

    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejenv_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static RecordBatch Lote(params (int id, int monto)[] filas) => new()
    {
        Columns = new[] { "id", "monto" },
        Rows = filas.Select(f => new Record
        {
            Values = new Dictionary<string, object?> { ["id"] = f.id, ["monto"] = f.monto }
        }).ToList()
    };

    private sealed record Armado(
        MetadataDbContext Meta, EjecutorEnvios Ejecutor, EfEnvioStore Envios, EfHistorialCorridas Historial,
        SqliteDataStore Data, FakeDestino Destino, Envio Envio) : IDisposable
    {
        public void Dispose() => Meta.Dispose();
    }

    private static async Task<Armado> ArmarAsync(bool conDatos = true, int filasPorLote = 5_000)
    {
        var meta = NuevoMeta();
        var envios = new EfEnvioStore(meta);
        var pipelines = new EfPipelineStore(meta);
        var historial = new EfHistorialCorridas(meta);

        var dataCs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejenvd_{Guid.NewGuid():N}.db")};Pooling=False";
        var data = new SqliteDataStore(dataCs);
        await data.EnsureTableAsync("destino", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        if (conDatos)
            await data.WriteBatchAsync("destino", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var pipe = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", TablaDestino = "destino",
            ModoEscritura = ModoEscritura.Agregar, FilasPorLote = 100, ColumnasClave = { "id" }
        };
        await pipelines.UpsertAsync(pipe, CancellationToken.None);

        var origen = new DataSource { Id = Guid.NewGuid(), Nombre = "SQL-CONTA", Tipo = TipoDataSource.SqlServer };
        var origenes = new FakeDataSourceStore(origen);

        var envio = new Envio
        {
            Id = Guid.NewGuid(), Nombre = "e", Habilitado = true, Pendiente = true,
            PipelineId = pipe.Id, DataSourceId = origen.Id, TablaDestino = "dbo.mw_x"
        };
        await envios.UpsertAsync(envio, CancellationToken.None);

        var destino = new FakeDestino();
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var ejecutor = new EjecutorEnvios(envios, pipelines, origenes, data, historial,
            new FakeDestinoFactory(destino), enc, filasPorLote);

        return new Armado(meta, ejecutor, envios, historial, data, destino, envio);
    }

    private static Task<Envio?> Recargar(Armado a) => a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None);

    [Fact]
    public async Task Empuja_las_filas_y_apaga_la_bandera()
    {
        using var a = await ArmarAsync();

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(2, a.Destino.Escritas.Count);
        var e = (await Recargar(a))!;
        Assert.False(e.Pendiente);
        Assert.Null(e.InicioEnCurso);
        Assert.NotNull(e.UltimoCreado);
        Assert.Equal(EstadoEnvio.AlDia, e.Estado);
    }

    [Fact]
    public async Task No_manda_la_columna_procesado_al_destino()
    {
        using var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.DoesNotContain(a.Destino.ColumnasCreadas, c => c.Nombre == ServingColumns.Procesado);
        Assert.All(a.Destino.Escritas, f => Assert.False(f.Values.ContainsKey(ServingColumns.Procesado)));
    }

    [Fact]
    public async Task Manda_key_y_creado_y_creado_viaja_como_fecha()
    {
        // SQLite devuelve _creado como texto. Al destino tiene que llegar como DateTime: mandar el
        // string crudo dejaria la conversion en manos de la cultura del servidor remoto.
        using var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.All(a.Destino.Escritas, f =>
        {
            Assert.True(f.Values.ContainsKey(ServingColumns.Key));
            Assert.IsType<DateTime>(f.Values[ServingColumns.Creado]);
        });
    }

    [Fact]
    public async Task Infiere_el_tipo_de_las_columnas_desde_el_primer_lote()
    {
        using var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var id = a.Destino.ColumnasCreadas.Single(c => c.Nombre == "id");
        Assert.Equal(typeof(long), id.TipoClr);
    }

    [Fact]
    public async Task Sin_filas_nuevas_apaga_la_bandera_y_no_toca_el_destino()
    {
        using var a = await ArmarAsync(conDatos: false);

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Empty(a.Destino.Escritas);
        Assert.Equal(0, a.Destino.VecesEnsure);
        var e = (await Recargar(a))!;
        Assert.False(e.Pendiente);
        Assert.Null(e.UltimoCreado);
    }

    [Fact]
    public async Task La_segunda_corrida_no_reenvia_lo_que_ya_mando()
    {
        using var a = await ArmarAsync();
        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);
        a.Destino.Escritas.Clear();

        await a.Envios.MarcarPendientesAsync(a.Envio.PipelineId, CancellationToken.None);
        await a.Ejecutor.ProcesarPendientesAsync(Ahora.AddMinutes(1), CancellationToken.None);

        Assert.Empty(a.Destino.Escritas);
    }

    [Fact]
    public async Task La_segunda_corrida_manda_solo_lo_nuevo()
    {
        using var a = await ArmarAsync();
        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);
        a.Destino.Escritas.Clear();

        await Task.Delay(50);
        await a.Data.WriteBatchAsync("destino", Lote((3, 300)), Clave, ModoEscritura.Agregar, CancellationToken.None);
        await a.Envios.MarcarPendientesAsync(a.Envio.PipelineId, CancellationToken.None);
        await a.Ejecutor.ProcesarPendientesAsync(Ahora.AddMinutes(1), CancellationToken.None);

        var fila = Assert.Single(a.Destino.Escritas);
        Assert.Equal(3L, Convert.ToInt64(fila.Values["id"]));
    }

    [Fact]
    public async Task Recorre_varios_lotes_por_keyset_sin_perder_ni_repetir()
    {
        // Lote de 2 sobre 5 filas: obliga a pedir el lote siguiente por (_creado, _key) tres veces.
        // Las 5 filas comparten _creado (un solo WriteBatch), asi que el desempate por _key es todo.
        using var a = await ArmarAsync(conDatos: false, filasPorLote: 2);
        await a.Data.WriteBatchAsync("destino", Lote((1, 1), (2, 2), (3, 3), (4, 4), (5, 5)), Clave,
            ModoEscritura.Agregar, CancellationToken.None);

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var ids = a.Destino.Escritas.Select(f => Convert.ToInt64(f.Values["id"])).ToList();
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, ids);
        var corrida = Assert.Single(await a.Historial.ObtenerRecientesAsync(10, CancellationToken.None),
            c => c.EnvioId == a.Envio.Id);
        Assert.Equal(5, corrida.FilasEscritas);
    }

    [Fact]
    public async Task Si_el_destino_falla_suma_intento_y_deja_pendiente()
    {
        using var a = await ArmarAsync();
        a.Destino.FallaAlEscribir = new InvalidOperationException("Login failed for user 'mw_push'.");

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        var e = (await Recargar(a))!;
        Assert.Equal(1, e.IntentosFallidos);
        Assert.True(e.Pendiente);
        Assert.Null(e.InicioEnCurso);
        Assert.Null(e.UltimoCreado);
        Assert.Equal(Ahora.AddMinutes(1), e.ProximoIntento);
        Assert.Contains("Login failed", e.UltimoError);
    }

    [Fact]
    public async Task Al_agotar_los_reintentos_se_deshabilita()
    {
        using var a = await ArmarAsync();
        a.Destino.FallaAlEscribir = new InvalidOperationException("sigue caido");
        var envio = (await Recargar(a))!;
        envio.IntentosFallidos = Envio.MaxIntentos - 1;
        envio.ProximoIntento = null;
        await a.Envios.UpsertAsync(envio, CancellationToken.None);

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var e = (await Recargar(a))!;
        Assert.False(e.Habilitado);
        Assert.Equal(Envio.MaxIntentos, e.IntentosFallidos);
        Assert.Equal(EstadoEnvio.Fallido, e.Estado);
    }

    [Fact]
    public async Task Registra_la_corrida_en_el_historial_con_el_EnvioId()
    {
        using var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var corridas = await a.Historial.ObtenerRecientesAsync(10, CancellationToken.None);
        var deEnvio = Assert.Single(corridas, c => c.EnvioId == a.Envio.Id);
        Assert.Equal(EstadoCorrida.Ok, deEnvio.Estado);
        Assert.Equal(2, deEnvio.FilasEscritas);
    }

    [Fact]
    public async Task Una_falla_queda_en_el_historial_con_el_numero_de_intento()
    {
        using var a = await ArmarAsync();
        a.Destino.FallaAlEscribir = new InvalidOperationException("no conecta");

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var corridas = await a.Historial.ObtenerRecientesAsync(10, CancellationToken.None);
        var deEnvio = Assert.Single(corridas, c => c.EnvioId == a.Envio.Id);
        Assert.Equal(EstadoCorrida.Fallida, deEnvio.Estado);
        Assert.StartsWith("Intento 1/5.", deEnvio.Mensaje);
    }
}
