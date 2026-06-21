using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]
[Route("api")]
public sealed class RunsController : ControllerBase
{
    private readonly IPipelineStore _store;
    private readonly IHistorialCorridas _historial;
    private readonly PipelineRunner _runner;
    private readonly IEncryptionService _encryption;
    private readonly PipelineHydrator _hydrator;

    public RunsController(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption, PipelineHydrator hydrator)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
        _hydrator = hydrator;
    }

    [HttpPost("run/{id:guid}")]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        var def = await _store.GetByIdAsync(id, ct);
        if (def is null) return NotFound($"Pipeline {id} no encontrado.");

        await _hydrator.HydrateAsync(def, ct);
        var runId = await _historial.IniciarAsync(def.Id, ct);
        var result = await _runner.RunAsync(def, new Dictionary<string, string>(), _encryption, ct);
        await _historial.CompletarAsync(runId, result.Success, result.RowsRead, result.RowsWritten, result.Error, ct);

        return Ok(new { result.RunId, result.Success, result.RowsRead, result.RowsWritten, result.Error });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> Runs(int max = 50, CancellationToken ct = default)
    {
        var recientes = await _historial.ObtenerRecientesAsync(Math.Clamp(max, 1, 500), ct);
        return Ok(recientes);
    }
}
