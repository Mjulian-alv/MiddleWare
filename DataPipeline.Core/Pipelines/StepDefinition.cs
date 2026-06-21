using System.Text.Json;

namespace DataPipeline.Core.Pipelines;

/// <summary>Un paso del pipeline (Origen o Transformación). El sink interno es implícito.</summary>
public sealed class StepDefinition
{
    public string Nombre { get; set; } = "";
    public TipoPaso Tipo { get; set; }
    public string Conector { get; set; } = "";

    /// <summary>Config propia del paso. Valores como JsonElement para admitir objetos anidados (transforms).</summary>
    public Dictionary<string, JsonElement> Configuracion { get; set; } = new();
}
