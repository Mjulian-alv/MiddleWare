using DataPipeline.Api.Controllers;
using DataPipeline.Connectors.Security;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataSourcesColumnasTests
{
    private static EncryptionService Enc() => new(Convert.ToBase64String(new byte[32]));

    private sealed class FakeDsStore : IDataSourceStore
    {
        private readonly DataSource? _ds;
        public FakeDsStore(DataSource? ds) => _ds = ds;
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(_ds);
        public Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DataSource>>(Array.Empty<DataSource>());
        public Task UpsertAsync(DataSource ds, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(false);
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - flags SqlLlamado/ArchivoLlamado para poder
    // assertar que Probar() rama por Tipo hacia el metodo correcto.
    private sealed class FakeTester : IConnectionTester
    {
        // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - DataHubLlamado: el ruteo por Tipo ahora tiene
        // tres ramas, y lo que importa es que un origen DataHub NO caiga en la de archivo.
        public bool SqlLlamado, ArchivoLlamado, DataHubLlamado;
        public Task<ConnectionResult> ProbarSqlServerAsync(string cs, CancellationToken ct)
        {
            SqlLlamado = true;
            return Task.FromResult(new ConnectionResult(true, null));
        }
        public Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct)
        {
            ArchivoLlamado = true;
            return Task.FromResult(new ConnectionResult(true, null));
        }
        public Task<ConnectionResult> ProbarDataHubAsync(OrigenDataHub origen, CancellationToken ct)
        {
            DataHubLlamado = true;
            return Task.FromResult(new ConnectionResult(true, null, "Catalogo OK: 3 endpoint(s)"));
        }
    }

    private sealed class FakeDiscoverer : ISchemaDiscoverer
    {
        private readonly SchemaResult _r;
        public FakeDiscoverer(SchemaResult r) => _r = r;
        public Task<SchemaResult> DescubrirSqlServerAsync(string cs, string sql, CancellationToken ct) =>
            Task.FromResult(_r);

        // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - implementacion minima para no romper el fake.
        public Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct) =>
            Task.FromResult(_r);

        // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - Columnas de un endpoint de DataHub.
        public bool DataHubLlamado;
        public string? RutaPedida;
        public bool EjecutarPedido;
        public Task<SchemaResult> DescubrirDataHubAsync(
            OrigenDataHub origen, string ruta, IReadOnlyDictionary<string, string>? parametros,
            bool ejecutarSiFaltaEsquema, CancellationToken ct)
        {
            DataHubLlamado = true;
            RutaPedida = ruta;
            EjecutarPedido = ejecutarSiFaltaEsquema;
            return Task.FromResult(_r);
        }
    }

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Fake del catalogo de DataHub.
    private sealed class FakeCatalogo : IDataHubCatalogo
    {
        private readonly CatalogoResult _r;
        public FakeCatalogo(CatalogoResult? r = null) =>
            _r = r ?? new CatalogoResult(true, new List<EndpointDataHub>
            {
                new("ventas/facturas", "GET", "Facturas emitidas", "Oracle - Produccion",
                    new List<ParametroEndpoint>
                    {
                        new("sucursal", "texto", "Sucursal", null, true),
                        new("watermark", "fechaHora", "Watermark", null, false)
                    },
                    """[{"name":"nro_factura","dataType":"texto"}]""")
            }, null);

        public Task<CatalogoResult> ListarAsync(string baseUrl, string u, string p, CancellationToken ct) =>
            Task.FromResult(_r);
    }

    private static DataSourcesController Controller(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString); // el controller desencripta
        return new DataSourcesController(new FakeDsStore(ds), enc, new FakeTester(),
            new FakeDiscoverer(r), new FakeCatalogo());
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - helper que expone el FakeTester usado, para
    // assertar cual rama de Probar() se llamo (SqlLlamado vs ArchivoLlamado).
    private static (DataSourcesController Ctrl, FakeTester Tester) ControllerConTester(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString);
        var tester = new FakeTester();
        return (new DataSourcesController(new FakeDsStore(ds), enc, tester,
            new FakeDiscoverer(r), new FakeCatalogo()), tester);
    }

    [Fact]
    public async Task Devuelve_las_columnas_descubiertas()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.SqlServer, ConnectionString = "Server=x;" };
        var cols = new List<ColumnaOrigen> { new() { Nombre = "id", Tipo = "int" } };
        var ctrl = Controller(ds, new SchemaResult(true, cols, null));

        var res = await ctrl.Columnas(ds.Id, new DataSourcesController.ColumnasReq("SELECT id FROM t"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var lista = Assert.IsAssignableFrom<IReadOnlyList<ColumnaOrigen>>(ok.Value);
        Assert.Single(lista);
        Assert.Equal("id", lista[0].Nombre);
    }

    [Fact]
    public async Task DataSource_inexistente_da_NotFound()
    {
        var ctrl = Controller(null, new SchemaResult(true, new List<ColumnaOrigen>(), null));
        var res = await ctrl.Columnas(Guid.NewGuid(), new DataSourcesController.ColumnasReq("SELECT 1"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(res);
    }

    [Fact]
    public async Task Error_del_discoverer_da_BadRequest()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.SqlServer, ConnectionString = "Server=x;" };
        var ctrl = Controller(ds, new SchemaResult(false, null, "sintaxis invalida"));
        var res = await ctrl.Columnas(ds.Id, new DataSourcesController.ColumnasReq("SELCT 1"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Probar/Columnas ramificados: origenes de
    // archivo (Csv/Xlsx) no requieren Sql y usan ProbarArchivoAsync/DescubrirArchivoAsync.
    [Fact]
    public async Task Columnas_de_un_DataSource_Csv_no_requiere_Sql()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.Csv, RutaCarpeta = @"C:\x" };
        var cols = new List<ColumnaOrigen> { new() { Nombre = "id", Tipo = "string" } };
        var ctrl = Controller(ds, new SchemaResult(true, cols, null));

        var res = await ctrl.Columnas(ds.Id, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var lista = Assert.IsAssignableFrom<IReadOnlyList<ColumnaOrigen>>(ok.Value);
        Assert.Single(lista);
    }

    [Fact]
    public async Task Probar_un_DataSource_Csv_usa_ProbarArchivoAsync()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.Csv, RutaCarpeta = @"C:\x" };
        var (ctrl, tester) = ControllerConTester(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        var res = await ctrl.Probar(ds.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(res);
        Assert.True(tester.ArchivoLlamado);
        Assert.False(tester.SqlLlamado);
    }

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 3 - El ruteo por Tipo era binario: todo lo que no
    // fuera SqlServer caia en la rama de archivo. Estas dos pruebas fijan que DataHub NO caiga ahi.
    [Fact]
    public async Task Probar_un_DataSource_DataHub_no_cae_en_la_rama_de_archivo()
    {
        var ds = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "dh", Tipo = TipoDataSource.DataHubCompLook,
            BaseUrl = "http://srv-datahub:5000"
        };
        var (ctrl, tester) = ControllerConTester(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        var res = await ctrl.Probar(ds.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(res);
        Assert.True(tester.DataHubLlamado);
        Assert.False(tester.ArchivoLlamado);
        Assert.False(tester.SqlLlamado);
    }

    [Fact]
    public async Task Columnas_de_un_DataHub_sin_endpoint_elegido_responde_400()
    {
        var ds = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "dh", Tipo = TipoDataSource.DataHubCompLook,
            BaseUrl = "http://srv-datahub:5000"
        };
        var ctrl = Controller(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        var res = await ctrl.Columnas(ds.Id, new DataSourcesController.ColumnasReq(null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task El_catalogo_solo_se_pide_para_origenes_DataHub()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "sql", Tipo = TipoDataSource.SqlServer };
        var ctrl = Controller(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        Assert.IsType<BadRequestObjectResult>(await ctrl.Endpoints(ds.Id, CancellationToken.None));
    }

    [Fact]
    public async Task El_catalogo_devuelve_los_endpoints_con_sus_parametros()
    {
        var ds = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "dh", Tipo = TipoDataSource.DataHubCompLook,
            BaseUrl = "http://srv-datahub:5000"
        };
        var ctrl = Controller(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Endpoints(ds.Id, CancellationToken.None));
        var lista = Assert.IsAssignableFrom<IReadOnlyList<EndpointDataHub>>(ok.Value);

        Assert.Single(lista);
        Assert.Equal("ventas/facturas", lista[0].Ruta);
        Assert.Equal("Oracle - Produccion", lista[0].Motor);
        Assert.Equal(2, lista[0].Parametros.Count);
        Assert.Contains(lista[0].Parametros, p => p.Nombre == "watermark");
    }
}
