namespace DataPipeline.Admin.Models;

/// <summary>Par clave/valor editable (para mapas rename/cast/computed).</summary>
public sealed class KV
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
}
