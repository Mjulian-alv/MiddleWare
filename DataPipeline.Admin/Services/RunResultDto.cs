namespace DataPipeline.Admin.Services;

/// <summary>Resultado de correr un pipeline on-demand (shape de POST /api/run/{id}).</summary>
public sealed record RunResultDto(Guid RunId, bool Success, long RowsRead, long RowsWritten, string? Error);
