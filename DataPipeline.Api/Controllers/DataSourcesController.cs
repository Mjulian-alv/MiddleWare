using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/datasources")]
public sealed class DataSourcesController : ControllerBase
{
    private readonly IDataSourceStore _store;
    private readonly IEncryptionService _enc;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc)
    {
        _store = store;
        _enc = enc;
    }

    public sealed record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString);

    public sealed record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo,
        string? ConnectionString, string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo);

    private static DataSourceVista AVista(DataSource d) =>
        new(d.Id, d.Nombre, d.Tipo, d.Activo, d.CreatedAt, d.UpdatedAt, !string.IsNullOrEmpty(d.ConnectionString));

    private string? Enc(string? plano) => string.IsNullOrEmpty(plano) ? null : _enc.Encrypt(plano);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok((await _store.GetAllAsync(ct)).Select(AVista));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        return d is null ? NotFound() : Ok(AVista(d));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] DataSourceUpsert req, CancellationToken ct)
    {
        var id = req.Id == Guid.Empty ? Guid.NewGuid() : req.Id;
        var existente = req.Id == Guid.Empty ? null : await _store.GetByIdAsync(id, ct);

        var ds = new DataSource
        {
            Id = id,
            Nombre = req.Nombre,
            Tipo = req.Tipo,
            ConnectionString = Enc(req.ConnectionString),
            Username = Enc(req.Username),
            Password = Enc(req.Password),
            BaseUrl = req.BaseUrl,
            FilePath = req.FilePath,
            Activo = req.Activo,
            CreatedAt = existente?.CreatedAt ?? DateTime.Now,
            UpdatedAt = existente is null ? null : DateTime.Now
        };
        await _store.UpsertAsync(ds, ct);
        return Ok(new { ds.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var borrado = await _store.DeleteAsync(id, ct);
        return borrado ? NoContent() : NotFound();
    }
}
