using System.Net.Http.Json;
using System.Text.Json;

namespace DataPipeline.Admin.Services;

// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origenes de archivo (Csv/Xlsx): campos de carpeta
// en los DTOs (reemplazan FilePath, sin uso); TraerColumnasAsync no exige sql para archivo.
// Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Origen DataHub CompLook: campos del origen, catalogo
// de endpoints, y el error de "traer columnas" deja de perderse (antes TraerColumnasAsync devolvia
// null y el motivo real se tiraba a la basura, justo cuando mas hace falta leerlo).
/// <summary>Vista de un DataSource (sin secretos; calca el shape que devuelve la API).</summary>
public sealed record DataSourceVista(Guid Id, string Nombre, int Tipo, bool Activo,
    DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString,
    string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
    bool TieneEncabezado, string? HojaExcel,
    string? BaseUrl, bool TieneCredencialCatalogo, bool TieneCredencialDatos);

/// <summary>Payload de alta/edicion (secretos en texto plano; vacio en edicion = conservar).</summary>
public sealed record DataSourceUpsert(Guid Id, string Nombre, int Tipo, string? ConnectionString,
    string? Username, string? Password, string? BaseUrl,
    string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
    bool TieneEncabezado, string? HojaExcel, bool Activo,
    string? UsuarioCatalogo, string? PasswordCatalogo);

/// <summary>Resultado de probar la conexion. Mensaje viaja tambien cuando sale bien.</summary>
public sealed record ProbarResp(bool Ok, string? Error, string? Mensaje);

/// <summary>Una columna del resultado del origen (nombre + tipo).</summary>
public sealed record ColumnaOrigenDto(string Nombre, string Tipo);

/// <summary>Resultado de traer columnas: si fallo, el motivo se conserva para mostrarlo.</summary>
public sealed record ColumnasResp(bool Ok, List<ColumnaOrigenDto> Columnas, string? Error);

/// <summary>Un parametro declarado por un endpoint de DataHub.</summary>
public sealed record ParametroEndpointDto(string Nombre, string Tipo, string? Etiqueta,
    string? Descripcion, bool Requerido);

/// <summary>Un endpoint del catalogo de DataHub, tal como se elige en el Paso 1.</summary>
public sealed record EndpointDataHubDto(string Ruta, string MetodoHttp, string? Descripcion,
    string Motor, List<ParametroEndpointDto> Parametros, string? EsquemaJson);

/// <summary>Resultado de pedir el catalogo: distingue "vino vacio" de "no se pudo traer".</summary>
public sealed record CatalogoResp(bool Ok, List<EndpointDataHubDto> Endpoints, string? Error);

/// <summary>Habla con DataPipeline.Api (rol admin) para gestionar DataSources.</summary>
public class DataSourcesAdminService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public DataSourcesAdminService(HttpClient http) => _http = http;

    public async Task<List<DataSourceVista>> ObtenerTodosAsync()
    {
        var r = await _http.GetAsync("api/datasources");
        if (!r.IsSuccessStatusCode) return new();
        return JsonSerializer.Deserialize<List<DataSourceVista>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }

    public async Task<DataSourceVista?> ObtenerPorIdAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/datasources/{id}");
        if (!r.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<DataSourceVista>(await r.Content.ReadAsStringAsync(), _json);
    }

    public async Task<bool> GuardarAsync(DataSourceUpsert ds)
    {
        var r = await _http.PostAsJsonAsync("api/datasources", ds);
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> EliminarAsync(Guid id)
    {
        var r = await _http.DeleteAsync($"api/datasources/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<ProbarResp> ProbarAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/datasources/{id}/probar", null);
        if (!r.IsSuccessStatusCode) return new ProbarResp(false, $"HTTP {(int)r.StatusCode}", null);
        return JsonSerializer.Deserialize<ProbarResp>(await r.Content.ReadAsStringAsync(), _json)
            ?? new ProbarResp(false, "Respuesta vacia.", null);
    }

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Catalogo de endpoints de DataHub.
    public async Task<CatalogoResp> ObtenerEndpointsAsync(Guid id)
    {
        HttpResponseMessage r;
        try
        {
            r = await _http.GetAsync($"api/datasources/{id}/endpoints");
        }
        catch (HttpRequestException ex)
        {
            return new CatalogoResp(false, new(), $"No se pudo hablar con la API: {ex.Message}");
        }

        var cuerpo = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
            return new CatalogoResp(false, new(), ErrorDe(cuerpo) ?? $"HTTP {(int)r.StatusCode}");

        var lista = JsonSerializer.Deserialize<List<EndpointDataHubDto>>(cuerpo, _json) ?? new();
        return new CatalogoResp(true, lista, null);
    }

    /// <summary>
    /// Trae las columnas del origen. Para DataHub se pasa la ruta del endpoint y, si se quiere el
    /// ultimo recurso de ejecutar la consulta, <paramref name="ejecutar"/> en true.
    /// </summary>
    public async Task<ColumnasResp> TraerColumnasAsync(
        Guid id, string? sql, string? ruta = null,
        Dictionary<string, string>? parametros = null, bool ejecutar = false)
    {
        HttpResponseMessage r;
        try
        {
            r = await _http.PostAsJsonAsync($"api/datasources/{id}/columnas",
                new { sql, ruta, parametros, ejecutar });
        }
        catch (HttpRequestException ex)
        {
            return new ColumnasResp(false, new(), $"No se pudo hablar con la API: {ex.Message}");
        }

        var cuerpo = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
            return new ColumnasResp(false, new(), ErrorDe(cuerpo) ?? $"HTTP {(int)r.StatusCode}");

        return new ColumnasResp(true,
            JsonSerializer.Deserialize<List<ColumnaOrigenDto>>(cuerpo, _json) ?? new(), null);
    }

    /// <summary>Saca el "error" del cuerpo que devuelve la API cuando responde 400.</summary>
    private static string? ErrorDe(string cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) return null;
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String)
                return e.GetString();
        }
        catch (JsonException) { /* no era json: se usa el fallback */ }
        return null;
    }
}
