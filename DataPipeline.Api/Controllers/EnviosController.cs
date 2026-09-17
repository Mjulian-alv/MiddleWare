// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: CRUD + reintento manual.
// Mismo molde que DataSourcesController, incluido el rol admin: sin el, el usuario pull (el
// consumidor externo) podria crear envios apuntados a cualquier servidor usando las credenciales ya
// guardadas en los origenes.
using DataPipeline.Connectors.Destinos;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/envios")]
public sealed class EnviosController : ControllerBase
{
    private readonly IEnvioStore _envios;
    public EnviosController(IEnvioStore envios) => _envios = envios;

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _envios.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        var envio = await _envios.GetByIdAsync(id, ct);
        return envio is null ? NotFound($"Envio {id} no encontrado.") : Ok(envio);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] Envio envio, CancellationToken ct) => await GuardarAsync(envio, ct);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] Envio envio, CancellationToken ct)
    {
        if (envio.Id != id) return BadRequest("El id del cuerpo no coincide con el de la ruta.");
        return await GuardarAsync(envio, ct);
    }

    private async Task<IActionResult> GuardarAsync(Envio envio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envio.Nombre)) return BadRequest("El nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(envio.TablaDestino)) return BadRequest("El nombre de la tabla destino es obligatorio.");
        if (envio.PipelineId == Guid.Empty) return BadRequest("Hay que elegir un pipeline.");
        if (envio.DataSourceId == Guid.Empty) return BadRequest("Hay que elegir una conexion destino.");

        // Se valida el nombre ahora y no en la primera corrida: si no, un error de tipeo fallaria cinco
        // veces en runtime y el envio se deshabilitaria solo, quince minutos despues de guardarlo.
        try { SqlServerDestinoSql.Partes(envio.TablaDestino); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }

        if (envio.Id == Guid.Empty) envio.Id = Guid.NewGuid();
        await _envios.UpsertAsync(envio, ct);
        return Ok(envio);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Borrar(Guid id, CancellationToken ct) =>
        await _envios.DeleteAsync(id, ct) ? Ok() : NotFound($"Envio {id} no encontrado.");

    // Reactiva un envio que agoto los reintentos y se deshabilito solo. Lo deja Pendiente: el barrido
    // del proximo tick lo levanta, no hace falta esperar a que corra el pipeline.
    [HttpPost("{id:guid}/reintentar")]
    public async Task<IActionResult> Reintentar(Guid id, CancellationToken ct)
    {
        if (await _envios.GetByIdAsync(id, ct) is null) return NotFound($"Envio {id} no encontrado.");
        await _envios.ReactivarAsync(id, ct);
        return Ok();
    }
}
