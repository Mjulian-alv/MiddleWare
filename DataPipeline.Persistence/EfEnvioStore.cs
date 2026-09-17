// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
// TomarPendientesAsync usa un UPDATE condicional por fila (ExecuteUpdateAsync con InicioEnCurso == null
// en el WHERE) en vez de SELECT + UPDATE: solo se lleva el envio quien gana la condicion, asi dos
// barridos concurrentes no pueden tomar el mismo.
//
// Las lecturas van sin tracking y UpsertAsync limpia el ChangeTracker al guardar. Motivo: casi todo
// el estado se escribe con ExecuteUpdateAsync, que pasa por fuera del change tracker. Si una lectura
// dejara la entidad trackeada, la siguiente consulta devolveria esa instancia cacheada con los
// valores de antes del UPDATE (identity resolution de EF), y reguardarla chocaria con la trackeada.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfEnvioStore : IEnvioStore
{
    private readonly MetadataDbContext _db;
    public EfEnvioStore(MetadataDbContext db) => _db = db;

    public Task<Envio?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Envios.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Envio>> GetAllAsync(CancellationToken ct) =>
        await _db.Envios.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<Envio>> GetByPipelineAsync(Guid pipelineId, CancellationToken ct) =>
        await _db.Envios.AsNoTracking().Where(e => e.PipelineId == pipelineId).ToListAsync(ct);

    public async Task UpsertAsync(Envio envio, CancellationToken ct)
    {
        var existe = await _db.Envios.AnyAsync(e => e.Id == envio.Id, ct);
        if (existe) _db.Envios.Update(envio);
        else _db.Envios.Add(envio);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await _db.Envios.Where(e => e.Id == id).ExecuteDeleteAsync(ct) > 0;

    public Task<int> MarcarPendientesAsync(Guid pipelineId, CancellationToken ct) =>
        _db.Envios
            .Where(e => e.PipelineId == pipelineId && e.Habilitado)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Pendiente, true), ct);

    public async Task<IReadOnlyList<Envio>> TomarPendientesAsync(DateTime ahora, CancellationToken ct)
    {
        var candidatos = await _db.Envios.AsNoTracking()
            .Where(e => e.Habilitado && e.Pendiente && e.InicioEnCurso == null
                     && (e.ProximoIntento == null || e.ProximoIntento <= ahora))
            .ToListAsync(ct);

        var tomados = new List<Envio>(candidatos.Count);
        foreach (var c in candidatos)
        {
            var filas = await _db.Envios
                .Where(e => e.Id == c.Id && e.InicioEnCurso == null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.InicioEnCurso, ahora), ct);
            if (filas != 1) continue;
            c.InicioEnCurso = ahora;
            tomados.Add(c);
        }
        return tomados;
    }

    public Task LiberarAsync(Guid id, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.InicioEnCurso, (DateTime?)null), ct);

    public Task RegistrarExitoAsync(Guid id, DateTime? ultimoCreado, DateTime fin, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.UltimoCreado, ultimoCreado)
                .SetProperty(e => e.UltimoEnvio, (DateTime?)fin)
                .SetProperty(e => e.Pendiente, false)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.IntentosFallidos, 0)
                .SetProperty(e => e.ProximoIntento, (DateTime?)null)
                .SetProperty(e => e.UltimoError, (string?)null), ct);

    public Task RegistrarFalloAsync(Guid id, string error, DateTime? proximoIntento, bool deshabilitar, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IntentosFallidos, e => e.IntentosFallidos + 1)
                .SetProperty(e => e.UltimoError, error)
                .SetProperty(e => e.ProximoIntento, proximoIntento)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.Habilitado, e => !deshabilitar && e.Habilitado), ct);

    public Task ReactivarAsync(Guid id, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Habilitado, true)
                .SetProperty(e => e.IntentosFallidos, 0)
                .SetProperty(e => e.ProximoIntento, (DateTime?)null)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.Pendiente, true), ct);
}
