namespace DataPipeline.Engine;

/// <summary>Resultado de una corrida de pipeline.</summary>
public sealed record RunResult(
    Guid RunId,
    bool Success,
    long RowsRead,
    long RowsWritten,
    string? Error,
    TimeSpan Duration,
    string? NuevoWatermark = null);
