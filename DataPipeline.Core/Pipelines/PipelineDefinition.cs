namespace DataPipeline.Core.Pipelines;

/// <summary>Definición declarativa de un pipeline (lo que se persiste/edita).</summary>
public sealed class PipelineDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public ScheduleKind Schedule { get; set; } = ScheduleKind.OnDemand;
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }

    public List<StepDefinition> Steps { get; set; } = new();
    public string TargetTable { get; set; } = "";
    public WriteMode WriteMode { get; set; } = WriteMode.Append;

    public List<Guid> RunAfter { get; set; } = new();

    public int BatchSize { get; set; } = 5_000;
}
