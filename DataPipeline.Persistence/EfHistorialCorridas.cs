using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfHistorialCorridas : IHistorialCorridas
{
    private readonly MetadataDbContext _db;
    public EfHistorialCorridas(MetadataDbContext db) => _db = db;

    public async Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct)
    {
        var run = new PipelineRun
        {
            Id = Guid.NewGuid(), PipelineId = pipelineId,
            Inicio = DateTime.Now, Estado = EstadoCorrida.EnCurso
        };
        _db.Corridas.Add(run);
        await _db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct)
    {
        var run = await _db.Corridas.FirstOrDefaultAsync(c => c.Id == runId, ct);
        if (run is null) return;
        run.Fin = DateTime.Now;
        run.Estado = exito ? EstadoCorrida.Ok : EstadoCorrida.Fallida;
        run.FilasLeidas = leidas;
        run.FilasEscritas = escritas;
        run.Mensaje = error;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct) =>
        await _db.Corridas.AsNoTracking().OrderByDescending(c => c.Inicio).Take(max).ToListAsync(ct);
}
