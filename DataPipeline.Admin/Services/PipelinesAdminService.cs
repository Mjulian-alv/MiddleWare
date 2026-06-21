using System.Net.Http.Json;
using System.Text.Json;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Admin.Services;

/// <summary>Habla con DataPipeline.Api (rol admin) para gestionar y correr pipelines.</summary>
public class PipelinesAdminService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PipelinesAdminService(HttpClient http) => _http = http;

    public async Task<List<PipelineDefinition>> ObtenerTodosAsync()
    {
        var r = await _http.GetAsync("api/pipelines");
        if (!r.IsSuccessStatusCode) return new();
        return JsonSerializer.Deserialize<List<PipelineDefinition>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }

    public async Task<PipelineDefinition?> ObtenerPorIdAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/pipelines/{id}");
        if (!r.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<PipelineDefinition>(await r.Content.ReadAsStringAsync(), _json);
    }

    public async Task<bool> GuardarAsync(PipelineDefinition def)
    {
        var r = await _http.PostAsJsonAsync("api/pipelines", def);
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> EliminarAsync(Guid id)
    {
        var r = await _http.DeleteAsync($"api/pipelines/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<RunResultDto?> CorrerAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/run/{id}", null);
        if (!r.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<RunResultDto>(await r.Content.ReadAsStringAsync(), _json);
    }
}
