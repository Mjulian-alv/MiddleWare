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

    private sealed class FakeTester : IConnectionTester
    {
        public Task<ConnectionResult> ProbarSqlServerAsync(string cs, CancellationToken ct) =>
            Task.FromResult(new ConnectionResult(true, null));
    }

    private sealed class FakeDiscoverer : ISchemaDiscoverer
    {
        private readonly SchemaResult _r;
        public FakeDiscoverer(SchemaResult r) => _r = r;
        public Task<SchemaResult> DescubrirSqlServerAsync(string cs, string sql, CancellationToken ct) =>
            Task.FromResult(_r);
    }

    private static DataSourcesController Controller(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString); // el controller desencripta
        return new DataSourcesController(new FakeDsStore(ds), enc, new FakeTester(), new FakeDiscoverer(r));
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
}
