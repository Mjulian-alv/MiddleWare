using System.Text.Json;
using System.Text.Json.Serialization;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Engine;

/// <summary>Carga una PipelineDefinition desde JSON (formato del apéndice A del spec).</summary>
public static class PipelineJsonLoader
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static PipelineDefinition Load(string json)
    {
        var def = JsonSerializer.Deserialize<PipelineDefinition>(json, _opts)
            ?? throw new InvalidOperationException("JSON de pipeline inválido (deserializó a null).");

        if (def.Id == Guid.Empty)
            def.Id = Guid.NewGuid();

        return def;
    }
}
