using DataPipeline.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]
[Route("api/data")]
public sealed class DataController : ControllerBase
{
    private readonly IPipelineStore _store;
    private readonly IDataStore _data;

    public DataController(IPipelineStore store, IDataStore data)
    {
        _store = store;
        _data = data;
    }

    [HttpGet("{nombre}")]
    public async Task<IActionResult> Get(string nombre, int page = 1, int pageSize = 100,
        bool incluirProcesados = false, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1) return BadRequest("page y pageSize deben ser >= 1.");

        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");

        try
        {
            var lote = await _data.ReadPagedAsync(def.TablaDestino, page, pageSize, incluirProcesados, ct);
            return Ok(new { columns = lote.Columns, rows = lote.Rows.Select(r => r.Values) });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aun no existe (el pipeline no corrio todavia).");
        }
    }

    public sealed record ProcesarRequest(List<string> Keys);

    [HttpPost("{nombre}/procesar")]
    public async Task<IActionResult> Procesar(string nombre, [FromBody] ProcesarRequest req, CancellationToken ct)
    {
        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");
        if (req?.Keys is null || req.Keys.Count == 0) return Ok(new { marcadas = 0 });

        try
        {
            var n = await _data.MarcarProcesadasAsync(def.TablaDestino, req.Keys, ct);
            return Ok(new { marcadas = n });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aun no existe.");
        }
    }
}
