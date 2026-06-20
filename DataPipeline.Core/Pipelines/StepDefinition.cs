namespace DataPipeline.Core.Pipelines;

/// <summary>Un paso del pipeline (Origen o Transformación). El sink interno es implícito.</summary>
public sealed class StepDefinition
{
    public string Nombre { get; set; } = "";
    public TipoPaso Tipo { get; set; }
    public string Conector { get; set; } = "";
    public Dictionary<string, string> Configuracion { get; set; } = new();
}
