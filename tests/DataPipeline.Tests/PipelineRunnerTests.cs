using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class PipelineRunnerTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dpr_{Guid.NewGuid():N}.db")};Pooling=False";

    private static EncryptionService EncDummy() =>
        new(Convert.ToBase64String(new byte[32]));

    private static Record Fila(int id, string nombre) =>
        new() { Values = new Dictionary<string, object?> { ["id"] = id, ["nombre"] = nombre } };

    private static PipelineDefinition DefMinima() => new()
    {
        Id = Guid.NewGuid(),
        Nombre = "p",
        TablaDestino = "destino",
        ModoEscritura = ModoEscritura.Agregar,
        FilasPorLote = 2,
        Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
    };

    [Fact]
    public async Task Corre_source_a_store_y_reporta_filas()
    {
        var cs = NuevaDb();
        var store = new SqliteDataStore(cs);
        var source = new FakeSource(
            new[] { "id", "nombre" },
            new[] { Fila(1, "a"), Fila(2, "b"), Fila(3, "c") },
            porLote: 2);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

        var result = await runner.RunAsync(DefMinima(), new Dictionary<string, string>(), EncDummy());

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.RowsRead);
        Assert.Equal(3, result.RowsWritten);

        var pagina = await store.ReadPagedAsync("destino", 1, 10, true, CancellationToken.None);
        Assert.Equal(3, pagina.Rows.Count);
    }

    [Fact]
    public async Task Falla_controlada_devuelve_Success_false_con_Error()
    {
        var store = new SqliteDataStore(NuevaDb());
        var def = DefMinima();
        def.Pasos.Clear(); // sin Origen -> InvalidOperationException, capturada
        var source = new FakeSource(new[] { "id" }, Array.Empty<Record>(), 5);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.False(result.Success);
        Assert.Contains("Origen", result.Error);
    }

    [Fact]
    public async Task Trackea_el_maximo_de_la_columna_watermark()
    {
        var cs = NuevaDb();
        var store = new SqliteDataStore(cs);
        var filas = new[]
        {
            new Record { Values = new Dictionary<string, object?> { ["id"] = 1, ["fecha_mod"] = new DateTime(2026, 6, 20, 18, 0, 0) } },
            new Record { Values = new Dictionary<string, object?> { ["id"] = 2, ["fecha_mod"] = new DateTime(2026, 6, 21, 10, 30, 0) } },
            new Record { Values = new Dictionary<string, object?> { ["id"] = 3, ["fecha_mod"] = (object?)null } },
        };
        var source = new FakeSource(new[] { "id", "fecha_mod" }, filas, porLote: 2);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

        var def = DefMinima();
        def.ColumnaWatermark = "fecha_mod";

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.True(result.Success, result.Error);
        Assert.Equal(new DateTime(2026, 6, 21, 10, 30, 0).ToString("O"), result.NuevoWatermark);
    }

    [Fact]
    public async Task Inyecta_UltimoWatermark_en_los_parametros_del_origen()
    {
        var store = new SqliteDataStore(NuevaDb());
        var spy = new SpyParamsSource(new[] { "id", "fecha_mod" });
        var runner = new PipelineRunner(new FakeSourceFactory(spy), new TransformFactory(), store);

        var def = DefMinima();
        def.ColumnaWatermark = "fecha_mod";
        def.UltimoWatermark = "2026-06-20T18:00:00.0000000";

        await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.True(spy.ParametrosVistos.ContainsKey("watermark"));
        Assert.Equal("2026-06-20T18:00:00.0000000", spy.ParametrosVistos["watermark"]);
    }
}
