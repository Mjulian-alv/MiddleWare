// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Descubrimiento de columnas para origenes de archivo.
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de descubrir el esquema del resultado de un SQL (sin traer datos).</summary>
public sealed record SchemaResult(bool Ok, IReadOnlyList<ColumnaOrigen>? Columnas, string? Error);

/// <summary>Descubre las columnas del resultado de un SELECT sin ejecutar la consulta para datos.</summary>
public interface ISchemaDiscoverer
{
    Task<SchemaResult> DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct);

    /// <summary>Csv/Xlsx: lee el encabezado del archivo sin procesar mas nuevo de <paramref name="ds"/>.</summary>
    Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct);
}
