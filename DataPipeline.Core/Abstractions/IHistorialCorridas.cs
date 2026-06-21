using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Historial de corridas (almacen A).</summary>
public interface IHistorialCorridas
{
    Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct);
    Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct);
    Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct);
}
