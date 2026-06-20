using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using Xunit;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class RecordBatcherTests
{
    private static async IAsyncEnumerable<Record> Generar(int cantidad, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < cantidad; i++)
        {
            await Task.Yield();
            yield return new Record { Values = new Dictionary<string, object?> { ["n"] = i } };
        }
    }

    [Fact]
    public async Task Agrupa_en_lotes_del_tamano_pedido_con_resto_final()
    {
        var cols = new[] { "n" };
        var lotes = new List<int>();
        await foreach (var lote in RecordBatcher.Batch(cols, Generar(7), tamanoLote: 3, CancellationToken.None))
            lotes.Add(lote.Rows.Count);

        Assert.Equal(new[] { 3, 3, 1 }, lotes);
    }

    [Fact]
    public async Task Secuencia_vacia_no_emite_lotes()
    {
        var cols = new[] { "n" };
        var lotes = 0;
        await foreach (var _ in RecordBatcher.Batch(cols, Generar(0), 5, CancellationToken.None))
            lotes++;

        Assert.Equal(0, lotes);
    }
}
