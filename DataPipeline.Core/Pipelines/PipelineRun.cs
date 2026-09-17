namespace DataPipeline.Core.Pipelines;

/// <summary>Estado de una corrida de pipeline.</summary>
public enum EstadoCorrida { EnCurso, Ok, Fallida }

/// <summary>Registro de una corrida (historial / observabilidad).</summary>
public sealed class PipelineRun
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: una sola tabla de
    // historial. Null = corrida de pipeline; con valor = corrida de envio de ese pipeline.
    public Guid? EnvioId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public EstadoCorrida Estado { get; set; }
    public long FilasLeidas { get; set; }
    public long FilasEscritas { get; set; }
    public long FilasError { get; set; }
    public string? Mensaje { get; set; }
}
