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
    public async Task<IActionResult> Get(string nombre, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1) return BadRequest("page y pageSize deben ser >= 1.");

        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");

        try
        {
            var lote = await _data.ReadPagedAsync(def.TablaDestino, page, pageSize, false, ct);
            return Ok(new { columns = lote.Columns, rows = lote.Rows.Select(r => r.Values) });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aun no existe (el pipeline no corrio todavia).");
        }
    }
}
