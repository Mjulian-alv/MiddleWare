using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Almacen de definiciones de pipeline (almacen A).</summary>
public interface IPipelineStore
{
    Task<PipelineDefinition?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PipelineDefinition?> GetByNombreAsync(string nombre, CancellationToken ct);
    Task<IReadOnlyList<PipelineDefinition>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PipelineDefinition def, CancellationToken ct);
    Task<IReadOnlyList<PipelineDefinition>> ObtenerVencidosAsync(DateTime ahora, CancellationToken ct);
    Task ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima, CancellationToken ct);
    Task ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
