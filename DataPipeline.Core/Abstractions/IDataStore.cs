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
    // Pagina por keyset y no por OFFSET: una corrida manual en paralelo que reescribe una fila en
    // Reemplazar la manda al final del orden y corre todas las posiciones; con OFFSET la fila que cae
    // en el hueco no se lee nunca y el cursor avanza igual.
    /// <summary>
    /// Filas estrictamente posteriores a la posicion (creadoDesde, keyDesde) en el orden (_creado, _key),
    /// hasta 'limite'. keyDesde null = todo lo posterior a creadoDesde. Ambos null = desde el principio.
    /// </summary>
    Task<RecordBatch> LeerDesdeAsync(string table, DateTime? creadoDesde, string? keyDesde, int limite, CancellationToken ct);
    Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct);
}
