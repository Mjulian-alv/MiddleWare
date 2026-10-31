// Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Catalogo de endpoints de DataHub.
// Se consulta en diseno, no al correr: sirve para elegir endpoint y saber que parametros declara.
namespace DataPipeline.Core.Abstractions;

/// <summary>Un parametro que el endpoint declara en DataHub.</summary>
/// <param name="Tipo">"texto", "numero", "fecha" o "fechaHora", segun el ParameterType de DataHub.</param>
public sealed record ParametroEndpoint(string Nombre, string Tipo, string? Etiqueta, string? Descripcion, bool Requerido);

/// <summary>Un endpoint publicado en DataHub, con lo necesario para configurarlo como origen.</summary>
/// <param name="Motor">Nombre del origen de datos en DataHub: es lo que dice si el endpoint es Oracle.</param>
/// <param name="EsquemaJson">
/// El <c>ResultSchemaJson</c> tal como lo guardo DataHub en su ultima prueba exitosa. Null o vacio
/// significa que el endpoint nunca paso una prueba: ahi no se pueden traer columnas sin ejecutar.
/// </param>
public sealed record EndpointDataHub(
    string Ruta,
    string MetodoHttp,
    string? Descripcion,
    string Motor,
    IReadOnlyList<ParametroEndpoint> Parametros,
    string? EsquemaJson);

/// <summary>Resultado de pedir el catalogo. El error viaja en texto, para mostrarlo tal cual.</summary>
public sealed record CatalogoResult(bool Ok, IReadOnlyList<EndpointDataHub>? Endpoints, string? Error);

/// <summary>
/// Un origen DataHub con sus secretos YA desencriptados, listo para hablar con DataHub.
/// Las dos credenciales son distintas a proposito: ver el comentario de DataHubCatalogo.
/// </summary>
public sealed record OrigenDataHub(
    string BaseUrl,
    string UsuarioCatalogo,
    string PasswordCatalogo,
    string UsuarioDatos,
    string PasswordDatos);

/// <summary>Lista los endpoints de DataHub usando la credencial de catalogo (rol Admin).</summary>
public interface IDataHubCatalogo
{
    Task<CatalogoResult> ListarAsync(
        string baseUrl, string usuarioCatalogo, string passwordCatalogo, CancellationToken ct);
}
