// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Leer el error que manda la API.
// Los servicios del Admin devolvian bool: "no fue 2xx". El motivo —que la API SI manda— se tiraba,
// y la pantalla mostraba un texto generico inventado en el Admin que no describia nada. Este helper
// es el unico lugar donde se decide como se lee un error, para que todos lo lean igual.
using System.Text.Json;

namespace DataPipeline.Admin.Services;

/// <summary>Resultado de una operacion contra la API, con el motivo cuando sale mal.</summary>
/// <param name="TraceId">El mismo identificador que quedo en el log del servidor, si lo mando.</param>
public sealed record ResultadoApi(bool Ok, string? Error, string? TraceId = null)
{
    public static readonly ResultadoApi Bien = new(true, null);

    /// <summary>El texto para mostrar: el motivo y, si lo hay, el traceId para buscarlo en el log.</summary>
    public string Mostrar() =>
        Ok ? "" : string.IsNullOrWhiteSpace(TraceId) ? (Error ?? "Error desconocido.")
                                                     : $"{Error} (traceId {TraceId})";
}

/// <summary>Convierte una respuesta HTTP fallida en un ResultadoApi con el motivo real.</summary>
public static class ErroresApi
{
    public static async Task<ResultadoApi> DeRespuestaAsync(HttpResponseMessage r)
    {
        if (r.IsSuccessStatusCode) return ResultadoApi.Bien;

        string cuerpo;
        try { cuerpo = await r.Content.ReadAsStringAsync(); }
        catch (HttpRequestException) { cuerpo = ""; }

        var (error, traceId) = Interpretar(cuerpo);

        // Sin cuerpo util, al menos se dice que paso de verdad en vez de inventar una causa.
        error ??= (int)r.StatusCode switch
        {
            401 => "La API rechazo las credenciales del panel (401). Revisar ApiAuth en appsettings.",
            403 => "El usuario del panel no tiene permiso para esta operacion (403).",
            404 => "La API no encontro el recurso (404).",
            409 => "Conflicto: probablemente el nombre ya existe (409).",
            >= 500 => $"La API fallo con {(int)r.StatusCode}. El detalle quedo en su log.",
            _ => $"La API respondio {(int)r.StatusCode} {r.ReasonPhrase}."
        };

        return new ResultadoApi(false, error, traceId);
    }

    /// <summary>
    /// La API puede contestar { error, traceId }, o un ProblemDetails de ASP.NET con "title" y
    /// "errors" (lo que devuelve la validacion del modelo). Se leen los dos.
    /// </summary>
    private static (string? Error, string? TraceId) Interpretar(string cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (Recortar(cuerpo), null);

            var raiz = doc.RootElement;
            string? traceId = Texto(raiz, "traceId");

            if (Texto(raiz, "error") is { Length: > 0 } e) return (e, traceId);

            // ProblemDetails: el detalle util suele estar en "errors", no en "title".
            if (raiz.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Object)
            {
                var partes = new List<string>();
                foreach (var campo in errs.EnumerateObject())
                {
                    if (campo.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var m in campo.Value.EnumerateArray())
                        if (m.ValueKind == JsonValueKind.String) partes.Add($"{campo.Name}: {m.GetString()}");
                }
                if (partes.Count > 0) return (string.Join(" · ", partes), traceId);
            }

            if (Texto(raiz, "detail") is { Length: > 0 } d) return (d, traceId);
            if (Texto(raiz, "title") is { Length: > 0 } t) return (t, traceId);

            return (Recortar(cuerpo), traceId);
        }
        catch (JsonException)
        {
            return (Recortar(cuerpo), null);
        }
    }

    private static string? Texto(JsonElement o, string nombre) =>
        o.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Recortar(string s) => s.Length > 500 ? s[..500] + "..." : s;
}
