using DataPipeline.Core.DataSources;

namespace DataPipeline.Core.Abstractions;

/// <summary>Almacen de DataSources (almacen A).</summary>
public interface IDataSourceStore
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(DataSource ds, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
