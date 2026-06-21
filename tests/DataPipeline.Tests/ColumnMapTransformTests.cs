using System.Globalization;
using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class ColumnMapTransformTests
{
    private static RunContext Ctx() => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static async IAsyncEnumerable<RecordBatch> UnLote(params Record[] filas)
    {
        await Task.Yield();
        yield return new RecordBatch
        {
            Columns = filas.Length > 0 ? filas[0].Values.Keys.ToArray() : Array.Empty<string>(),
            Rows = filas
        };
    }

    private static Record Fila(params (string c, object? v)[] vals) =>
        new() { Values = vals.ToDictionary(x => x.c, x => x.v) };

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Rename_cambia_el_nombre_de_la_columna()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string> { ["monto"] = "importe" },
            cast: new Dictionary<string, string>(),
            computed: new Dictionary<string, string>(),
            filter: null);

        var filas = await Drenar(t.TransformAsync(UnLote(Fila(("monto", 10))), Ctx(), CancellationToken.None));

        Assert.True(filas[0].Values.ContainsKey("importe"));
        Assert.False(filas[0].Values.ContainsKey("monto"));
        Assert.Equal(10, filas[0].Values["importe"]);
    }

    [Fact]
    public async Task Cast_convierte_el_tipo()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string>(),
            cast: new Dictionary<string, string> { ["importe"] = "decimal" },
            computed: new Dictionary<string, string>(),
            filter: null);

        var filas = await Drenar(t.TransformAsync(UnLote(Fila(("importe", "10.5"))), Ctx(), CancellationToken.None));

        Assert.Equal(10.5m, filas[0].Values["importe"]);
    }
}
