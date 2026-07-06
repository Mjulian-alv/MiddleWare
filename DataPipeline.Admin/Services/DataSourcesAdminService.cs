using System.Net.Http.Json;
using System.Text.Json;

namespace DataPipeline.Admin.Services;

/// <summary>Vista de un DataSource (sin secretos; calca el shape que devuelve la API).</summary>
public sealed record DataSourceVista(Guid Id, string Nombre, int Tipo, bool Activo,
    DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString);

/// <summary>Payload de alta/edicion (secretos en texto plano; vacio en edicion = conservar).</summary>
public sealed record DataSourceUpsert(Guid Id, string Nombre, int Tipo, string? ConnectionString,
    string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo);

/// <summary>Resultado de probar la conexion.</summary>
public sealed record ProbarResp(bool Ok, string? Error);

/// <summary>Una columna del resultado del origen (nombre + tipo).</summary>
public sealed record ColumnaOrigenDto(string Nombre, string Tipo);

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
        if (!r.IsSuccessStatusCode) return new ProbarResp(false, $"HTTP {(int)r.StatusCode}");
        return JsonSerializer.Deserialize<ProbarResp>(await r.Content.ReadAsStringAsync(), _json)
            ?? new ProbarResp(false, "Respuesta vacia.");
    }

    public async Task<List<ColumnaOrigenDto>?> TraerColumnasAsync(Guid id, string sql)
    {
        var r = await _http.PostAsJsonAsync($"api/datasources/{id}/columnas", new { sql });
        if (!r.IsSuccessStatusCode) return null; // null = error (SQL invalido, conexion, etc.)
        return JsonSerializer.Deserialize<List<ColumnaOrigenDto>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }
}
