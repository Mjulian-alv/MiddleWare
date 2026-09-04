using DataPipeline.Core.DataSources;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class DataSourceRepoTests
{
    private static MetadataDbContext NuevoCtx()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"dsmeta_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static DataSource Ds(string nombre) => new()
    {
        Id = Guid.NewGuid(), Nombre = nombre, Tipo = TipoDataSource.SqlServer,
        ConnectionString = "cipher", Activo = true, CreatedAt = DateTime.Now
    };

    [Fact]
    public async Task Upsert_GetById_GetAll_roundtrip()
    {
        using var ctx = NuevoCtx();
        var store = new EfDataSourceStore(ctx);
        var ds = Ds("ventas");
        await store.UpsertAsync(ds, CancellationToken.None);

        var leido = await store.GetByIdAsync(ds.Id, CancellationToken.None);
        var todos = await store.GetAllAsync(CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Equal("ventas", leido!.Nombre);
        Assert.Equal(TipoDataSource.SqlServer, leido.Tipo);
        Assert.Single(todos);
    }

    [Fact]
    public async Task Delete_borra_y_devuelve_true_o_false()
    {
        using var ctx = NuevoCtx();
        var store = new EfDataSourceStore(ctx);
        var ds = Ds("borrar");
        await store.UpsertAsync(ds, CancellationToken.None);

        var ok = await store.DeleteAsync(ds.Id, CancellationToken.None);
        var leido = await store.GetByIdAsync(ds.Id, CancellationToken.None);
        var no = await store.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(ok);
        Assert.Null(leido);
        Assert.False(no);
    }

    [Fact]
    public async Task Persiste_los_campos_de_archivo_para_Csv_y_Xlsx()
    {
        using var ctx = NuevoCtx();
        var store = new EfDataSourceStore(ctx);
        var csv = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "ventas-csv", Tipo = TipoDataSource.Csv,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.csv",
            Separador = ";", Encoding = "Windows-1252", TieneEncabezado = true,
            Activo = true, CreatedAt = DateTime.Now
        };
        var xlsx = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "ventas-xlsx", Tipo = TipoDataSource.Xlsx,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.xlsx", HojaExcel = "Datos",
            Activo = true, CreatedAt = DateTime.Now
        };
        await store.UpsertAsync(csv, CancellationToken.None);
        await store.UpsertAsync(xlsx, CancellationToken.None);

        var leidoCsv = await store.GetByIdAsync(csv.Id, CancellationToken.None);
        var leidoXlsx = await store.GetByIdAsync(xlsx.Id, CancellationToken.None);

        Assert.Equal(TipoDataSource.Csv, leidoCsv!.Tipo);
        Assert.Equal(@"\\erp\exports", leidoCsv.RutaCarpeta);
        Assert.Equal("VENTAS_*.csv", leidoCsv.PatronArchivo);
        Assert.Equal(";", leidoCsv.Separador);
        Assert.Equal("Windows-1252", leidoCsv.Encoding);
        Assert.True(leidoCsv.TieneEncabezado);

        Assert.Equal(TipoDataSource.Xlsx, leidoXlsx!.Tipo);
        Assert.Equal("Datos", leidoXlsx.HojaExcel);
    }
}
