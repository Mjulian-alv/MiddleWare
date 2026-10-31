using DataPipeline.Core.Abstractions;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>Abre una conexion al origen y la cierra; cualquier excepcion -> Ok=false con el mensaje.</summary>
public sealed class ConnectionTester : IConnectionTester
{
    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - Probar un origen DataHub es pedirle el catalogo.
    private readonly IDataHubCatalogo _catalogo;

    public ConnectionTester(IDataHubCatalogo? catalogo = null) => _catalogo = catalogo ?? new DataHubCatalogo();

    public async Task<ConnectionResult> ProbarDataHubAsync(OrigenDataHub origen, CancellationToken ct)
    {
        var r = await _catalogo.ListarAsync(origen.BaseUrl, origen.UsuarioCatalogo, origen.PasswordCatalogo, ct);
        if (!r.Ok) return new ConnectionResult(false, r.Error);

        var endpoints = r.Endpoints ?? Array.Empty<EndpointDataHub>();
        if (endpoints.Count == 0)
            return new ConnectionResult(true, null,
                "El catalogo respondio, pero no trajo ningun endpoint activo. "
                + "Hay que publicar y activar endpoints en DataHub antes de usarlos como origen.");

        var motores = endpoints.Select(e => e.Motor)
                               .Where(m => !string.IsNullOrWhiteSpace(m))
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();
        var conEsquema = endpoints.Count(e => !string.IsNullOrWhiteSpace(e.EsquemaJson));

        // Mensaje "ok" con contenido: cuantos hay, de que motores, y cuantos ya tienen esquema
        // guardado (los que NO lo tienen obligan a ejecutar la consulta para traer columnas).
        return new ConnectionResult(true, null,
            $"Catalogo OK: {endpoints.Count} endpoint(s) activo(s)"
            + (motores.Count > 0 ? $" sobre {string.Join(", ", motores)}" : "")
            + $". {conEsquema} con esquema guardado. "
            + "La credencial de datos se verifica al traer columnas o al correr el pipeline.");
    }

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
