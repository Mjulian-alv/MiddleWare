using System.Globalization;
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Connectors.Transforms;

/// <summary>
/// Transform declarativo. Orden de operaciones por fila: rename -> cast -> computed -> filter.
/// (computed/filter se completan en la tarea siguiente con NCalc.)
/// </summary>
public sealed class ColumnMapTransform : ITransform
{
    private readonly IReadOnlyDictionary<string, string> _rename;
    private readonly IReadOnlyDictionary<string, string> _cast;
    private readonly IReadOnlyDictionary<string, string> _computed;
    private readonly string? _filter;

    public ColumnMapTransform(
        IReadOnlyDictionary<string, string> rename,
        IReadOnlyDictionary<string, string> cast,
        IReadOnlyDictionary<string, string> computed,
        string? filter)
    {
        _rename = rename;
        _cast = cast;
        _computed = computed;
        _filter = filter;
    }

    public async IAsyncEnumerable<RecordBatch> TransformAsync(
        IAsyncEnumerable<RecordBatch> input, RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var lote in input.WithCancellation(ct))
        {
            var salida = new List<Record>(lote.Rows.Count);
            foreach (var fila in lote.Rows)
            {
                var v = AplicarRename(fila.Values);
                AplicarCast(v);
                // computed/filter: Task 3
                salida.Add(new Record { Values = v });
            }

            var columnas = salida.Count > 0
                ? salida[0].Values.Keys.ToArray()
                : RenombrarColumnas(lote.Columns);

            yield return new RecordBatch { Columns = columnas, Rows = salida };
        }
    }

    private Dictionary<string, object?> AplicarRename(IReadOnlyDictionary<string, object?> origen)
    {
        var dst = new Dictionary<string, object?>(origen.Count);
        foreach (var (k, val) in origen)
            dst[_rename.TryGetValue(k, out var nuevo) ? nuevo : k] = val;
        return dst;
    }

    private string[] RenombrarColumnas(IReadOnlyList<string> cols) =>
        cols.Select(c => _rename.TryGetValue(c, out var n) ? n : c).ToArray();

    private void AplicarCast(Dictionary<string, object?> fila)
    {
        foreach (var (col, tipo) in _cast)
        {
            if (!fila.TryGetValue(col, out var val) || val is null) continue;
            fila[col] = Convertir(val, tipo);
        }
    }

    private static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch
    {
        "int"     => Convert.ToInt32(val, CultureInfo.InvariantCulture),
        "long"    => Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "decimal" => Convert.ToDecimal(val, CultureInfo.InvariantCulture),
        "double"  => Convert.ToDouble(val, CultureInfo.InvariantCulture),
        "bool"    => Convert.ToBoolean(val, CultureInfo.InvariantCulture),
        "date"    => DateTime.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        "string"  => Convert.ToString(val, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo de cast no soportado: '{tipo}'.")
    };
}
