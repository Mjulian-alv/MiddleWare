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

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Columnas de un endpoint de DataHub.
    /// <summary>
    /// Saca las columnas del <c>ResultSchemaJson</c> que DataHub ya tiene guardado, SIN ejecutar la
    /// consulta (decision D3). Si el endpoint nunca paso una prueba en DataHub no hay esquema:
    /// con <paramref name="ejecutarSiFaltaEsquema"/> se corre el endpoint de verdad como ultimo
    /// recurso; sin eso, vuelve Ok=false explicando que falta probarlo en DataHub.
    /// </summary>
    Task<SchemaResult> DescubrirDataHubAsync(
        OrigenDataHub origen,
        string ruta,
        IReadOnlyDictionary<string, string>? parametros,
        bool ejecutarSiFaltaEsquema,
        CancellationToken ct);
}
