namespace DataPipeline.Core.Pipelines;

/// <summary>Una columna del resultado del origen, descubierta en diseno (nombre + tipo SQL).</summary>
public sealed class ColumnaOrigen
{
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = ""; // nombre del tipo del provider (ej. "datetime", "int", "nvarchar")
}
