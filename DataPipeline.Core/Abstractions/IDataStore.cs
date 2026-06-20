using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Sink interno + lectura para la entrega. Aísla el motor de la BD concreta.</summary>
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columns, WriteMode mode, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, CancellationToken ct);
}
