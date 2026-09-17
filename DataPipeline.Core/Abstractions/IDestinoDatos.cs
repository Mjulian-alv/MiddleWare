// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
// Deliberadamente NO es un IDataStore: un destino externo solo se escribe. No se lee, no se ackea,
// no tiene _procesado. Meterlo en IDataStore obligaria a implementar tres metodos sin sentido.
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Columna a crear en el destino. TipoClr null = no se pudo inferir, va como texto.</summary>
public sealed record ColumnaDestino(string Nombre, Type? TipoClr);

/// <summary>Escritura en una tabla nuestra alojada en un servidor externo.</summary>
public interface IDestinoDatos
{
    Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct);

    /// <summary>Upsert del lote por _key. Devuelve las filas escritas.</summary>
    Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct);
}
