using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Historial de corridas (almacen A).</summary>
public interface IHistorialCorridas
{
    Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct);

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: una sola tabla de
    // historial para corridas de pipeline y de envio, distinguidas por EnvioId.
    Task<Guid> IniciarEnvioAsync(Guid pipelineId, Guid envioId, CancellationToken ct);
    Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct);
    Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct);
}
