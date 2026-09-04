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
        public bool SqlLlamado, ArchivoLlamado;
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
    }

    private static DataSourcesController Controller(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString); // el controller desencripta
        return new DataSourcesController(new FakeDsStore(ds), enc, new FakeTester(), new FakeDiscoverer(r));
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - helper que expone el FakeTester usado, para
    // assertar cual rama de Probar() se llamo (SqlLlamado vs ArchivoLlamado).
    private static (DataSourcesController Ctrl, FakeTester Tester) ControllerConTester(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString);
        var tester = new FakeTester();
        return (new DataSourcesController(new FakeDsStore(ds), enc, tester, new FakeDiscoverer(r)), tester);
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
}
