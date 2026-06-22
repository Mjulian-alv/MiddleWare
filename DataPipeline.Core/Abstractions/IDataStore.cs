using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Sink interno + lectura/ack para la entrega PULL con ciclo de procesado.</summary>
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct);
    Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct);
}
