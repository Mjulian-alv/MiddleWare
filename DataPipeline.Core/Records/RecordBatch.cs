namespace DataPipeline.Core.Records;

/// <summary>Un lote de registros que viaja entre pasos del pipeline.</summary>
public sealed record RecordBatch
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<Record> Rows { get; init; }
}
