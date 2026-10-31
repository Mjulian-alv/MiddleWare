// Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Catalogo de endpoints de DataHub.
// GET /api/endpoints exige rol Admin en DataHub ([Authorize(Policy = Politicas.Admin)]), que NO es
// la misma credencial con la que despues se leen los datos: /api/query valida a mano contra las
// asignaciones del endpoint y el admin no pasa por ahi. De ahi que el origen guarde dos credenciales.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DataPipeline.Core.Abstractions;

namespace DataPipeline.Connectors.Testing;

/// <summary>Cliente del catalogo de DataHub. Cualquier fallo vuelve como Ok=false con el texto.</summary>
public sealed class DataHubCatalogo : IDataHubCatalogo
{
    private readonly HttpMessageHandler? _handler;

    public DataHubCatalogo(HttpMessageHandler? handler = null) => _handler = handler;

    public async Task<CatalogoResult> ListarAsync(
        string baseUrl, string usuarioCatalogo, string passwordCatalogo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new CatalogoResult(false, null, "La URL de DataHub esta vacia.");

        try
        {
            using var http = _handler is null
                ? new HttpClient()
                : new HttpClient(_handler, disposeHandler: false);
            http.Timeout = TimeSpan.FromSeconds(30);

            using var pedido = new HttpRequestMessage(
                HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/endpoints");
            pedido.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{usuarioCatalogo}:{passwordCatalogo}")));

            using var respuesta = await http.SendAsync(pedido, ct);

            if (!respuesta.IsSuccessStatusCode)
                return new CatalogoResult(false, null, Describir(respuesta.StatusCode));

            var json = await respuesta.Content.ReadAsStringAsync(ct);
            return new CatalogoResult(true, Mapear(json), null);
        }
        catch (JsonException ex)
        {
            return new CatalogoResult(false, null,
                $"DataHub respondio algo que no es el catalogo esperado: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CatalogoResult(false, null, "DataHub no respondio dentro de los 30 segundos.");
        }
        catch (HttpRequestException ex)
        {
            return new CatalogoResult(false, null, $"No se pudo conectar con DataHub: {ex.Message}");
        }
    }

    private static string Describir(HttpStatusCode codigo) => (int)codigo switch
    {
        401 => "DataHub rechazo la credencial del catalogo (401). Tiene que ser un usuario "
               + "administrador de DataHub: un ApiUser comun no puede listar endpoints.",
        403 => "La credencial del catalogo autentico pero no tiene permiso de administrador (403).",
        404 => "La URL no expone /api/endpoints (404). Revisar que apunte a la raiz de DataHub.",
        _ => $"DataHub respondio {(int)codigo} al pedir el catalogo."
    };

    /// <summary>
    /// Mapea la respuesta de DataHub. Se lee con JsonDocument y no con un DTO para no romperse si
    /// DataHub agrega campos: de su respuesta solo nos importan seis cosas.
    /// </summary>
    private static List<EndpointDataHub> Mapear(string json)
    {
        var lista = new List<EndpointDataHub>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return lista;

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;

            // Los apagados no se ofrecen: elegir uno daria 404 recien al correr.
            if (Bool(e, "isActive") == false) continue;

            var ruta = Texto(e, "route");
            if (string.IsNullOrWhiteSpace(ruta)) continue;

            lista.Add(new EndpointDataHub(
                Ruta: ruta!.Trim('/'),
                MetodoHttp: string.IsNullOrWhiteSpace(Texto(e, "httpMethod")) ? "GET" : Texto(e, "httpMethod")!.ToUpperInvariant(),
                Descripcion: Texto(e, "description"),
                Motor: Texto(e, "dataSourceName") ?? "",
                Parametros: Parametros(e),
                EsquemaJson: Texto(e, "resultSchemaJson")));
        }

        return lista.OrderBy(x => x.Ruta, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<ParametroEndpoint> Parametros(JsonElement endpoint)
    {
        var ps = new List<ParametroEndpoint>();
        if (!Buscar(endpoint, "parameterDefinitions", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ps;

        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var nombre = Texto(p, "name");
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            ps.Add(new ParametroEndpoint(
                Nombre: nombre!,
                Tipo: Tipo(p),
                Etiqueta: Texto(p, "label"),
                Descripcion: Texto(p, "descripcion"),
                Requerido: Bool(p, "requerido") ?? true));
        }
        return ps;
    }

    /// <summary>
    /// DataHub no configura JsonStringEnumConverter, asi que ParameterType viaja como numero
    /// (0=Text, 1=Number, 2=Date, 3=DateTime). Se acepta tambien el nombre por si eso cambia.
    /// </summary>
    private static string Tipo(JsonElement parametro)
    {
        if (!Buscar(parametro, "type", out var t)) return "texto";

        if (t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n))
            return n switch { 1 => "numero", 2 => "fecha", 3 => "fechaHora", _ => "texto" };

        return t.GetString()?.ToLowerInvariant() switch
        {
            "number" => "numero",
            "date" => "fecha",
            "datetime" => "fechaHora",
            _ => "texto"
        };
    }

    private static bool Buscar(JsonElement obj, string nombre, out JsonElement valor)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase))
            {
                valor = p.Value;
                return true;
            }
        }
        valor = default;
        return false;
    }

    private static string? Texto(JsonElement obj, string nombre) =>
        Buscar(obj, nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement obj, string nombre) =>
        Buscar(obj, nombre, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
