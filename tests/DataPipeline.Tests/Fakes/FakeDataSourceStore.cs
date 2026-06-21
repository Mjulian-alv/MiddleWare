using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;

namespace DataPipeline.Tests.Fakes;

internal sealed class FakeDataSourceStore : IDataSourceStore
{
    private readonly Dictionary<Guid, DataSource> _datos = new();
    public FakeDataSourceStore(params DataSource[] ds) { foreach (var d in ds) _datos[d.Id] = d; }

    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_datos.TryGetValue(id, out var d) ? d : null);
    public Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult((IReadOnlyList<DataSource>)_datos.Values.ToList());
    public Task UpsertAsync(DataSource ds, CancellationToken ct) { _datos[ds.Id] = ds; return Task.CompletedTask; }
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(_datos.Remove(id));
}
