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

    // Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Devuelven el motivo, no un bool. Guardar un
    // pipeline y que la pantalla solo dijera "no se pudo guardar" obligaba a adivinar que paso.
    public async Task<ResultadoApi> GuardarAsync(PipelineDefinition def)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("api/pipelines", def);
            return await ErroresApi.DeRespuestaAsync(r);
        }
        catch (HttpRequestException ex)
        {
            return new ResultadoApi(false, $"No se pudo hablar con la API: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new ResultadoApi(false, "La API no respondio a tiempo al guardar.");
        }
    }

    public async Task<ResultadoApi> EliminarAsync(Guid id)
    {
        try
        {
            var r = await _http.DeleteAsync($"api/pipelines/{id}");
            return await ErroresApi.DeRespuestaAsync(r);
        }
        catch (HttpRequestException ex)
        {
            return new ResultadoApi(false, $"No se pudo hablar con la API: {ex.Message}");
        }
    }

    /// <summary>Corre el pipeline. Si falla, el motivo viene en Error en vez de volver null.</summary>
    public async Task<(RunResultDto? Corrida, ResultadoApi Resultado)> CorrerAsync(Guid id)
    {
        try
        {
            var r = await _http.PostAsync($"api/run/{id}", null);
            var res = await ErroresApi.DeRespuestaAsync(r);
            if (!res.Ok) return (null, res);

            var dto = JsonSerializer.Deserialize<RunResultDto>(await r.Content.ReadAsStringAsync(), _json);
            return dto is null
                ? (null, new ResultadoApi(false, "La API devolvio una respuesta vacia al correr."))
                : (dto, ResultadoApi.Bien);
        }
        catch (HttpRequestException ex)
        {
            return (null, new ResultadoApi(false, $"No se pudo hablar con la API: {ex.Message}"));
        }
        catch (TaskCanceledException)
        {
            return (null, new ResultadoApi(false, "La corrida supero el tiempo de espera del panel."));
        }
    }
}
