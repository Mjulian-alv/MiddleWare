// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Prueba de origenes de archivo (Csv/Xlsx).
namespace DataPipeline.Core.Abstractions;

// Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - Mensaje: una prueba que sale bien tambien tiene algo
// que contar (cuantos endpoints, de que motores). Opcional, para no tocar a los que ya existian.
/// <summary>Resultado de probar una conexion a un origen.</summary>
public sealed record ConnectionResult(bool Ok, string? Error, string? Mensaje = null);

/// <summary>Prueba de conectividad contra un origen (no lee datos).</summary>
public interface IConnectionTester
{
    Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct);

    /// <summary>Valida que la carpeta exista y sea escribible (necesario para crear \procesados).</summary>
    Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct);

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - Probar un origen DataHub CompLook.
    /// <summary>
    /// Prueba la credencial del CATALOGO pidiendo <c>GET /api/endpoints</c>. La credencial de datos
    /// no se prueba aca: la unica forma seria ejecutar un endpoint, y eso corre una consulta de
    /// verdad contra la base de ellos. Se verifica al traer columnas o al correr el pipeline.
    /// </summary>
    Task<ConnectionResult> ProbarDataHubAsync(OrigenDataHub origen, CancellationToken ct);
}
