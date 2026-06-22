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

    private static RecordBatch Lote(params (int id, int monto)[] filas) => new()
    {
        Columns = new[] { "id", "monto" },
        Rows = filas.Select(f => new Record
        {
            Values = new Dictionary<string, object?> { ["id"] = f.id, ["monto"] = f.monto }
        }).ToList()
    };

    private static readonly string[] Clave = { "id" };

    [Fact]
    public async Task Agregar_inserta_con_control_y_lee_no_procesadas()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var pagina = await store.ReadPagedAsync("t", 1, 10, false, CancellationToken.None);

        Assert.Equal(2, pagina.Rows.Count);
        var fila = pagina.Rows[0];
        Assert.True(fila.Values.ContainsKey(ServingColumns.Key));
        Assert.True(fila.Values.ContainsKey(ServingColumns.Creado));
        Assert.Equal(0L, Convert.ToInt64(fila.Values[ServingColumns.Procesado]));
    }

    [Fact]
    public async Task Agregar_borra_las_procesadas_en_la_siguiente_corrida()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        await store.MarcarProcesadasAsync("t", new[] { "1" }, CancellationToken.None);
        // Siguiente corrida: EnsureTable (Agregar) borra las procesadas; luego inserta lo nuevo
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((3, 300)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var todas = await store.ReadPagedAsync("t", 1, 10, true, CancellationToken.None);
        var ids = todas.Rows.Select(r => Convert.ToInt64(r.Values["id"])).OrderBy(x => x).ToArray();
        Assert.Equal(new long[] { 2, 3 }, ids); // la 1 (procesada) se borro
    }

    [Fact]
    public async Task Reemplazar_upsert_por_key()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 200)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var pagina = await store.ReadPagedAsync("t", 1, 10, true, CancellationToken.None);
        Assert.Single(pagina.Rows);
        Assert.Equal(200L, Convert.ToInt64(pagina.Rows[0].Values["monto"]));
    }

    [Fact]
    public async Task Reemplazar_coincidencia_vuelve_procesado_a_cero()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.MarcarProcesadasAsync("t", new[] { "1" }, CancellationToken.None);

        await store.WriteBatchAsync("t", Lote((1, 150)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var noProcesadas = await store.ReadPagedAsync("t", 1, 10, false, CancellationToken.None);
        Assert.Single(noProcesadas.Rows); // volvio a _procesado=0 al coincidir
        Assert.Equal(150L, Convert.ToInt64(noProcesadas.Rows[0].Values["monto"]));
    }

    [Fact]
    public async Task MarcarProcesadas_devuelve_la_cantidad_y_filtra_en_lectura()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var marcadas = await store.MarcarProcesadasAsync("t", new[] { "1" }, CancellationToken.None);
        var noProcesadas = await store.ReadPagedAsync("t", 1, 10, false, CancellationToken.None);

        Assert.Equal(1, marcadas);
        Assert.Single(noProcesadas.Rows);
        Assert.Equal(2L, Convert.ToInt64(noProcesadas.Rows[0].Values["id"]));
    }

    [Fact]
    public async Task Reemplazar_sin_columnas_clave_lanza()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", new[] { "id" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteBatchAsync("t", Lote((1, 100)), Array.Empty<string>(), ModoEscritura.Reemplazar, CancellationToken.None));
    }
}
