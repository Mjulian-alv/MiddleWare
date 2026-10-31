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
    private readonly IDataHubCatalogo _catalogo;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc, IConnectionTester tester,
        ISchemaDiscoverer discoverer, IDataHubCatalogo catalogo)
    {
        _store = store;
        _enc = enc;
        _tester = tester;
        _discoverer = discoverer;
        _catalogo = catalogo;
    }

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 2 - Origen DataHub CompLook: BaseUrl se muestra (no
    // es secreto) y las dos credenciales viajan solo como "la tengo cargada", nunca su valor.
    public sealed record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel,
        string? BaseUrl, bool TieneCredencialCatalogo, bool TieneCredencialDatos);

    public sealed record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo,
        string? ConnectionString, string? Username, string? Password, string? BaseUrl,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel, bool Activo,
        string? UsuarioCatalogo, string? PasswordCatalogo);

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Revision 1, hallazgo P1: el metodo es parte de la
    // identidad del endpoint en DataHub, no un detalle.
    public sealed record ColumnasReq(string? Sql, string? Ruta = null,
        Dictionary<string, string>? Parametros = null, bool Ejecutar = false, string? Metodo = null);

    private static DataSourceVista AVista(DataSource d) =>
        new(d.Id, d.Nombre, d.Tipo, d.Activo, d.CreatedAt, d.UpdatedAt, !string.IsNullOrEmpty(d.ConnectionString),
            d.RutaCarpeta, d.PatronArchivo, d.Separador, d.Encoding, d.TieneEncabezado, d.HojaExcel,
            d.BaseUrl, !string.IsNullOrEmpty(d.UsuarioCatalogo), !string.IsNullOrEmpty(d.Username));

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
            // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 2 - Misma regla que los demas secretos:
            // vacio en una edicion significa conservar el que ya estaba.
            UsuarioCatalogo = Secreto(req.UsuarioCatalogo, existente?.UsuarioCatalogo),
            PasswordCatalogo = Secreto(req.PasswordCatalogo, existente?.PasswordCatalogo),
            Activo = req.Activo,
            CreatedAt = existente?.CreatedAt ?? DateTime.Now,
            UpdatedAt = existente is null ? null : DateTime.Now
        };
        await _store.UpsertAsync(ds, ct);
        return Ok(new { ds.Id });
    }

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 2 - Catalogo de endpoints para elegir en el Admin.
    // Las credenciales nunca vuelven al cliente: se desencriptan aca y mueren en esta llamada.
    [HttpGet("{id:guid}/endpoints")]
    public async Task<IActionResult> Endpoints(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();
        if (d.Tipo != TipoDataSource.DataHubCompLook)
            return BadRequest(new { error = "El origen no es de tipo DataHub CompLook." });

        var r = await _catalogo.ListarAsync(
            d.BaseUrl ?? "", Desencriptar(d.UsuarioCatalogo), Desencriptar(d.PasswordCatalogo), ct);

        return r.Ok ? Ok(r.Endpoints) : BadRequest(new { error = r.Error });
    }

    /// <summary>Arma el origen con los secretos en claro. No sale de la API.</summary>
    private OrigenDataHub Origen(DataSource d) => new(
        d.BaseUrl ?? "",
        Desencriptar(d.UsuarioCatalogo),
        Desencriptar(d.PasswordCatalogo),
        Desencriptar(d.Username),
        Desencriptar(d.Password));

    private string Desencriptar(string? valor) =>
        string.IsNullOrEmpty(valor) ? "" : _enc.Decrypt(valor);

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

        // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Esto era "si no es SqlServer, probalo como
        // archivo". Con un tercer tipo, un origen DataHub se probaba como carpeta de red y el error
        // hablaba de una ruta que nadie habia escrito. Ahora cada tipo tiene su rama.
        var r = d.Tipo switch
        {
            TipoDataSource.SqlServer => await _tester.ProbarSqlServerAsync(
                string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString), ct),
            TipoDataSource.Csv or TipoDataSource.Xlsx => await _tester.ProbarArchivoAsync(d.RutaCarpeta ?? "", ct),
            TipoDataSource.DataHubCompLook => await _tester.ProbarDataHubAsync(Origen(d), ct),
            _ => new ConnectionResult(false, $"Tipo de origen no soportado: {d.Tipo}.")
        };
        return Ok(new { ok = r.Ok, error = r.Error, mensaje = r.Mensaje });
    }

    [HttpPost("{id:guid}/columnas")]
    public async Task<IActionResult> Columnas(Guid id, [FromBody] ColumnasReq? req, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();

        // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Mismo cambio que en Probar: switch por Tipo.
        SchemaResult r;
        switch (d.Tipo)
        {
            case TipoDataSource.Csv:
            case TipoDataSource.Xlsx:
                r = await _discoverer.DescubrirArchivoAsync(d, ct);
                break;

            case TipoDataSource.SqlServer:
                if (string.IsNullOrWhiteSpace(req?.Sql)) return BadRequest(new { error = "SQL vacio." });
                var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
                r = await _discoverer.DescubrirSqlServerAsync(cs, req.Sql, ct);
                break;

            case TipoDataSource.DataHubCompLook:
                if (string.IsNullOrWhiteSpace(req?.Ruta))
                    return BadRequest(new { error = "No se eligio ningun endpoint." });
                r = await _discoverer.DescubrirDataHubAsync(
                    Origen(d), req.Ruta, req.Metodo, req.Parametros, req.Ejecutar, ct);
                break;

            default:
                return BadRequest(new { error = $"Tipo de origen no soportado: {d.Tipo}." });
        }
        return r.Ok ? Ok(r.Columnas) : BadRequest(new { error = r.Error });
    }
}
