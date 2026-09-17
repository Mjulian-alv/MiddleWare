using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Sink interno + lectura/ack para la entrega PULL con ciclo de procesado.</summary>
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct);

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor para el
    // push. A diferencia de ReadPagedAsync NO filtra por _procesado: el push es un consumidor con su
    // propia memoria (UltimoCreado) y no le puede pisar las filas a los consumidores PULL.
    Task<RecordBatch> LeerDesdeAsync(string table, DateTime? creadoDesde, int page, int pageSize, CancellationToken ct);
    Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct);
}
