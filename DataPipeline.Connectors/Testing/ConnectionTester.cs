using DataPipeline.Core.Abstractions;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>Abre una conexion al origen y la cierra; cualquier excepcion -> Ok=false con el mensaje.</summary>
public sealed class ConnectionTester : IConnectionTester
{
    public async Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var con = new SqlConnection(connectionString);
            await con.OpenAsync(ct);
            return new ConnectionResult(true, null);
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ex.Message);
        }
    }
}
