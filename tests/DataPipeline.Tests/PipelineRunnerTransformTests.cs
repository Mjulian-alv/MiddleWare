using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class PipelineRunnerTransformTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dprt_{Guid.NewGuid():N}.db")};Pooling=False";

    private static EncryptionService EncDummy() => new(Convert.ToBase64String(new byte[32]));

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Record Fila(int importe) =>
        new() { Values = new Dictionary<string, object?> { ["importe"] = importe } };

    [Fact]
    public async Task Aplica_filter_entre_source_y_store()
    {
        var store = new SqliteDataStore(NuevaDb());
        var source = new FakeSource(new[] { "importe" }, new[] { Fila(5), Fila(-2), Fila(8) }, porLote: 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(),
            Nombre = "p",
            TablaDestino = "destino",
            ModoEscritura = ModoEscritura.Reemplazar,
            FilasPorLote = 10,
            Pasos =
            {
                new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" },
                new StepDefinition
                {
                    Nombre = "filtrar", Tipo = TipoPaso.Transformacion, Conector = "ColumnMapTransform",
                    Configuracion = new() { ["filter"] = J("\"importe > 0\"") }
                }
            }
        };

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.RowsRead);
        Assert.Equal(2, result.RowsWritten);

        var pagina = await store.ReadPagedAsync("destino", 1, 10, CancellationToken.None);
        Assert.Equal(2, pagina.Rows.Count);
    }
}
