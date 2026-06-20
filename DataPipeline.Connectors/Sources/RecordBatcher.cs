using System.Runtime.CompilerServices;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Sources;

/// <summary>Agrupa una secuencia perezosa de Records en RecordBatch de tamaño fijo.</summary>
public static class RecordBatcher
{
    public static async IAsyncEnumerable<RecordBatch> Batch(
        IReadOnlyList<string> columnas,
        IAsyncEnumerable<Record> filas,
        int tamanoLote,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (tamanoLote <= 0) tamanoLote = 5_000;

        var buffer = new List<Record>(tamanoLote);
        await foreach (var fila in filas.WithCancellation(ct))
        {
            buffer.Add(fila);
            if (buffer.Count >= tamanoLote)
            {
                yield return new RecordBatch { Columns = columnas, Rows = buffer };
                buffer = new List<Record>(tamanoLote);
            }
        }
        if (buffer.Count > 0)
            yield return new RecordBatch { Columns = columnas, Rows = buffer };
    }
}
