// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo: el Admin no toca la BD, habla
// con la API por HttpClient. Mismo molde que DataSourcesAdminService.
using System.Net.Http.Json;
using System.Text.Json;
using DataPipeline.Core.Envios;

namespace DataPipeline.Admin.Services;

/// <summary>Habla con DataPipeline.Api (rol admin) para gestionar envios.</summary>
public class EnviosAdminService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public EnviosAdminService(HttpClient http) => _http = http;

    public async Task<List<Envio>> ObtenerTodosAsync()
    {
        var r = await _http.GetAsync("api/envios");
        if (!r.IsSuccessStatusCode) return new();
        return JsonSerializer.Deserialize<List<Envio>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }

    public async Task<Envio?> ObtenerPorIdAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/envios/{id}");
        if (!r.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<Envio>(await r.Content.ReadAsStringAsync(), _json);
    }

    /// <summary>Alta o edicion. Error trae el mensaje de validacion de la API tal cual.</summary>
    public async Task<(bool Ok, string? Error)> GuardarAsync(Envio envio)
    {
        var r = await _http.PostAsJsonAsync("api/envios", envio);
        return r.IsSuccessStatusCode ? (true, null) : (false, await r.Content.ReadAsStringAsync());
    }

    public async Task<bool> EliminarAsync(Guid id) =>
        (await _http.DeleteAsync($"api/envios/{id}")).IsSuccessStatusCode;

    /// <summary>Reactiva un envio fallido y lo deja pendiente: el proximo tick lo levanta.</summary>
    public async Task<bool> ReintentarAsync(Guid id) =>
        (await _http.PostAsync($"api/envios/{id}/reintentar", null)).IsSuccessStatusCode;
}
