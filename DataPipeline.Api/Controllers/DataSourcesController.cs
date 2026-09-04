// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origenes de archivo (Csv/Xlsx): DataSourceUpsert
// suma los campos de carpeta (reemplaza FilePath, sin uso); Probar/Columnas se ramifican por Tipo.
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
    private readonly IConnectionTester _tester;
    private readonly ISchemaDiscoverer _discoverer;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc, IConnectionTester tester, ISchemaDiscoverer discoverer)
    {
        _store = store;
        _enc = enc;
        _tester = tester;
        _discoverer = discoverer;
    }

    public sealed record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel);

    public sealed record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo,
        string? ConnectionString, string? Username, string? Password, string? BaseUrl,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel, bool Activo);

    public sealed record ColumnasReq(string? Sql);

    private static DataSourceVista AVista(DataSource d) =>
        new(d.Id, d.Nombre, d.Tipo, d.Activo, d.CreatedAt, d.UpdatedAt, !string.IsNullOrEmpty(d.ConnectionString),
            d.RutaCarpeta, d.PatronArchivo, d.Separador, d.Encoding, d.TieneEncabezado, d.HojaExcel);

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

        // Secreto vacio en update = conservar el anterior (evita borrarlo al editar otros campos).
        string? Secreto(string? plano, string? anterior) =>
            string.IsNullOrEmpty(plano) ? anterior : _enc.Encrypt(plano);

        var ds = new DataSource
        {
            Id = id,
            Nombre = req.Nombre,
            Tipo = req.Tipo,
            ConnectionString = Secreto(req.ConnectionString, existente?.ConnectionString),
            Username = Secreto(req.Username, existente?.Username),
            Password = Secreto(req.Password, existente?.Password),
            BaseUrl = req.BaseUrl,
            RutaCarpeta = req.RutaCarpeta,
            PatronArchivo = req.PatronArchivo,
            Separador = req.Separador,
            Encoding = req.Encoding,
            TieneEncabezado = req.TieneEncabezado,
            HojaExcel = req.HojaExcel,
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

    [HttpPost("{id:guid}/probar")]
    public async Task<IActionResult> Probar(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();

        if (d.Tipo != TipoDataSource.SqlServer)
        {
            var rArchivo = await _tester.ProbarArchivoAsync(d.RutaCarpeta ?? "", ct);
            return Ok(new { ok = rArchivo.Ok, error = rArchivo.Error });
        }

        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _tester.ProbarSqlServerAsync(cs, ct);
        return Ok(new { ok = r.Ok, error = r.Error });
    }

    [HttpPost("{id:guid}/columnas")]
    public async Task<IActionResult> Columnas(Guid id, [FromBody] ColumnasReq? req, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();

        if (d.Tipo != TipoDataSource.SqlServer)
        {
            var rArchivo = await _discoverer.DescubrirArchivoAsync(d, ct);
            return rArchivo.Ok ? Ok(rArchivo.Columnas) : BadRequest(new { error = rArchivo.Error });
        }

        if (string.IsNullOrWhiteSpace(req?.Sql)) return BadRequest(new { error = "SQL vacio." });
        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _discoverer.DescubrirSqlServerAsync(cs, req.Sql, ct);
        return r.Ok ? Ok(r.Columnas) : BadRequest(new { error = r.Error });
    }
}
