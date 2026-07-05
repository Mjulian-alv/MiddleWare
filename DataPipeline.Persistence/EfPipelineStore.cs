using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfPipelineStore : IPipelineStore
{
    private readonly MetadataDbContext _db;
    public EfPipelineStore(MetadataDbContext db) => _db = db;

    public Task<PipelineDefinition?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PipelineDefinition?> GetByNombreAsync(string nombre, CancellationToken ct) =>
        _db.Pipelines.FirstOrDefaultAsync(p => p.Nombre == nombre, ct);

    public async Task<IReadOnlyList<PipelineDefinition>> GetAllAsync(CancellationToken ct) =>
        await _db.Pipelines.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(PipelineDefinition def, CancellationToken ct)
    {
        var existe = await _db.Pipelines.AnyAsync(p => p.Id == def.Id, ct);
        if (existe) _db.Pipelines.Update(def);
        else _db.Pipelines.Add(def);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineDefinition>> ObtenerVencidosAsync(DateTime ahora, CancellationToken ct) =>
        await _db.Pipelines.AsNoTracking()
            .Where(p => p.Habilitado && p.ProximaCorrida != null && p.ProximaCorrida <= ahora)
            .ToListAsync(ct);

    public async Task ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return;
        def.UltimaCorrida = ultima;
        def.ProximaCorrida = proxima;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return;
        def.UltimoWatermark = watermark;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return false;
        _db.Pipelines.Remove(def);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
