// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Prueba de origenes de archivo (Csv/Xlsx).
namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de probar una conexion a un origen.</summary>
public sealed record ConnectionResult(bool Ok, string? Error);

/// <summary>Prueba de conectividad contra un origen (no lee datos).</summary>
public interface IConnectionTester
{
    Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct);

    /// <summary>Valida que la carpeta exista y sea escribible (necesario para crear \procesados).</summary>
    Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct);
}
