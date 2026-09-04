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

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origenes de archivo: valida que la carpeta
    // exista y sea escribible (ahi se crea \procesados al correr el pipeline).
    public async Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rutaCarpeta))
                return new ConnectionResult(false, "La ruta de la carpeta esta vacia.");
            if (!Directory.Exists(rutaCarpeta))
                return new ConnectionResult(false, $"No se encontro la carpeta: {rutaCarpeta}");

            var carpetaProcesados = Path.Combine(rutaCarpeta, "procesados");
            Directory.CreateDirectory(carpetaProcesados);
            var archivoPrueba = Path.Combine(carpetaProcesados, $".probar_{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(archivoPrueba, "", ct);
            File.Delete(archivoPrueba);

            return new ConnectionResult(true, null);
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ex.Message);
        }
    }
}
