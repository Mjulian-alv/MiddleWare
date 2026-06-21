namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de probar una conexion a un origen.</summary>
public sealed record ConnectionResult(bool Ok, string? Error);

/// <summary>Prueba de conectividad contra un origen (no lee datos).</summary>
public interface IConnectionTester
{
    Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct);
}
