namespace DataPipeline.Core.Pipelines;

/// <summary>Un paso del pipeline (Source o Transform). El sink interno es implícito.</summary>
public sealed class StepDefinition
{
    public string Name { get; set; } = "";
    public StepKind Kind { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, string> Config { get; set; } = new();
}
