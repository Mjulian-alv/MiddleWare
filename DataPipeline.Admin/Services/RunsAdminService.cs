using System.Text.Json;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Admin.Services;

/// <summary>Lee el historial de corridas desde DataPipeline.Api (rol admin).</summary>
public class RunsAdminService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public RunsAdminService(HttpClient http) => _http = http;

    public async Task<List<PipelineRun>> ObtenerRecientesAsync(int max = 50)
    {
        var r = await _http.GetAsync($"api/runs?max={max}");
        if (!r.IsSuccessStatusCode) return new();
        return JsonSerializer.Deserialize<List<PipelineRun>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }
}
