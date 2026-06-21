using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/pipelines")]
public sealed class PipelinesController : ControllerBase
{
    private readonly IPipelineStore _store;
    public PipelinesController(IPipelineStore store) => _store = store;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _store.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var def = await _store.GetByIdAsync(id, ct);
        return def is null ? NotFound() : Ok(def);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PipelineDefinition def, CancellationToken ct)
    {
        if (def.Id == Guid.Empty) def.Id = Guid.NewGuid();
        await _store.UpsertAsync(def, ct);
        return Ok(new { def.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var borrado = await _store.DeleteAsync(id, ct);
        return borrado ? NoContent() : NotFound();
    }
}
