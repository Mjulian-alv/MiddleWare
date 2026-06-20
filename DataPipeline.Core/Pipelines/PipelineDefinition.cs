namespace DataPipeline.Core.Pipelines;

/// <summary>Definición declarativa de un pipeline (lo que se persiste/edita).</summary>
public sealed class PipelineDefinition
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Habilitado { get; set; } = true;

    public TipoProgramacion Programacion { get; set; } = TipoProgramacion.BajoDemanda;
    public string? HoraDelDia { get; set; }
    public int? DiaDeLaSemana { get; set; }
    public int? DiaDelMes { get; set; }
    public DateTime? ProximaCorrida { get; set; }
    public DateTime? UltimaCorrida { get; set; }

    public List<StepDefinition> Pasos { get; set; } = new();
    public string TablaDestino { get; set; } = "";
    public ModoEscritura ModoEscritura { get; set; } = ModoEscritura.Reemplazar;

    public List<Guid> CorrerDespuesDe { get; set; } = new();

    public int FilasPorLote { get; set; } = 5_000;
}
