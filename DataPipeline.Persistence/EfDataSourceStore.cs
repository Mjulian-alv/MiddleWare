using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfDataSourceStore : IDataSourceStore
{
    private readonly MetadataDbContext _db;
    public EfDataSourceStore(MetadataDbContext db) => _db = db;

    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Origenes.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
        await _db.Origenes.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(DataSource ds, CancellationToken ct)
    {
        var existe = await _db.Origenes.AnyAsync(d => d.Id == ds.Id, ct);
        if (existe) _db.Origenes.Update(ds);
        else _db.Origenes.Add(ds);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ds = await _db.Origenes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (ds is null) return false;
        _db.Origenes.Remove(ds);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
