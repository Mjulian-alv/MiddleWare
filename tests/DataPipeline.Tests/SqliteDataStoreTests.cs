using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class SqliteDataStoreTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dpt_{Guid.NewGuid():N}.db")};Pooling=False";

    private static RecordBatch Lote(params (int id, string nombre)[] filas) => new()
    {
        Columns = new[] { "id", "nombre" },
        Rows = filas.Select(f => new Record
        {
            Values = new Dictionary<string, object?> { ["id"] = f.id, ["nombre"] = f.nombre }
        }).ToList()
    };

    [Fact]
    public async Task Append_escribe_y_ReadPaged_devuelve_las_filas()
    {
        var store = new SqliteDataStore(NuevaDb());
        var cols = new[] { "id", "nombre" };

        await store.EnsureTableAsync("t", cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, "a"), (2, "b")), CancellationToken.None);

        var pagina = await store.ReadPagedAsync("t", page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(2, pagina.Rows.Count);
        Assert.Equal(1L, Convert.ToInt64(pagina.Rows[0].Values["id"]));
        Assert.Equal("a", pagina.Rows[0].Values["nombre"]);
    }

    [Fact]
    public async Task Replace_recrea_la_tabla_descartando_lo_anterior()
    {
        var cs = NuevaDb();
        var store = new SqliteDataStore(cs);
        var cols = new[] { "id", "nombre" };

        await store.EnsureTableAsync("t", cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, "viejo")), CancellationToken.None);

        await store.EnsureTableAsync("t", cols, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((2, "nuevo")), CancellationToken.None);

        var pagina = await store.ReadPagedAsync("t", 1, 10, CancellationToken.None);
        Assert.Single(pagina.Rows);
        Assert.Equal("nuevo", pagina.Rows[0].Values["nombre"]);
    }

    [Fact]
    public async Task Upsert_todavia_no_soportado_lanza()
    {
        var store = new SqliteDataStore(NuevaDb());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            store.EnsureTableAsync("t", new[] { "id" }, ModoEscritura.Combinar, CancellationToken.None));
    }
}
