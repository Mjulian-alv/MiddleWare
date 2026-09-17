// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor.
// _creado se refresca en el upsert de Reemplazar, asi que sirve de cursor de cambios: una fila
// modificada vuelve a entrar en el barrido. Y no se filtra por _procesado, para no competir con
// los consumidores PULL.
using System.Globalization;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class SqliteDataStoreCursorTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dpcur_{Guid.NewGuid():N}.db")};Pooling=False";

    private static RecordBatch Lote(params (int id, int monto)[] filas) => new()
    {
        Columns = new[] { "id", "monto" },
        Rows = filas.Select(f => new Record
        {
            Values = new Dictionary<string, object?> { ["id"] = f.id, ["monto"] = f.monto }
        }).ToList()
    };

    private static readonly string[] Clave = { "id" };
    private static readonly string[] Cols = { "id", "monto" };

    // _creado vuelve de SQLite como texto (la tabla no declara tipos). Se parsea con cultura
    // invariante: con la cultura del servidor el mismo string puede leerse distinto.
    private static DateTime Creado(Record fila) => fila.Values[ServingColumns.Creado] switch
    {
        DateTime d => d,
        string s => DateTime.Parse(s, CultureInfo.InvariantCulture),
        var v => throw new InvalidOperationException($"_creado con tipo inesperado: {v?.GetType()}")
    };

    // DateTime.Now en Windows avanza de a ~1-16 ms: una espera corta alcanza para que dos escrituras
    // no caigan en el mismo instante.
    private static Task Separar() => Task.Delay(50);

    [Fact]
    public async Task Sin_cursor_devuelve_todo()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var lote = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);

        Assert.Equal(2, lote.Rows.Count);
    }

    [Fact]
    public async Task Con_cursor_devuelve_solo_lo_escrito_despues()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var primera = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);
        var cursor = Creado(primera.Rows[0]);

        await Separar();
        await store.WriteBatchAsync("t", Lote((2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var nuevas = await store.LeerDesdeAsync("t", cursor, null, 10, CancellationToken.None);

        Assert.Single(nuevas.Rows);
        Assert.Equal(2L, Convert.ToInt64(nuevas.Rows[0].Values["id"]));
    }

    [Fact]
    public async Task Una_fila_modificada_vuelve_a_entrar_en_el_barrido()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var primera = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);
        var cursor = Creado(primera.Rows[0]);

        await Separar();
        await store.WriteBatchAsync("t", Lote((1, 999)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var nuevas = await store.LeerDesdeAsync("t", cursor, null, 10, CancellationToken.None);

        Assert.Single(nuevas.Rows);
        Assert.Equal(999L, Convert.ToInt64(nuevas.Rows[0].Values["monto"]));
    }

    [Fact]
    public async Task El_cursor_exacto_no_devuelve_la_fila_que_lo_origino()
    {
        // El filtro es estricto (>): releer con el cursor recien guardado no puede reenviar nada.
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var primera = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);
        var otra = await store.LeerDesdeAsync("t", Creado(primera.Rows[0]), null, 10, CancellationToken.None);

        Assert.Empty(otra.Rows);
    }

    [Fact]
    public async Task No_filtra_por_procesado()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var todas = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);
        var keys = todas.Rows.Select(r => (string)r.Values[ServingColumns.Key]!).ToList();
        await store.MarcarProcesadasAsync("t", keys, CancellationToken.None);

        var despues = await store.LeerDesdeAsync("t", null, null, 10, CancellationToken.None);

        Assert.Equal(2, despues.Rows.Count);
    }

    // Recorre toda la tabla por keyset, como lo hace el envio, llamando a 'entrePaginas' despues de
    // cada pagina. Devuelve los ids en el orden en que se leyeron.
    private static async Task<List<long>> RecorrerAsync(SqliteDataStore store, int porPagina, Func<Task>? entrePaginas = null)
    {
        var ids = new List<long>();
        DateTime? creado = null;
        string? key = null;
        while (true)
        {
            var pagina = await store.LeerDesdeAsync("t", creado, key, porPagina, CancellationToken.None);
            if (pagina.Rows.Count == 0) break;
            ids.AddRange(pagina.Rows.Select(r => Convert.ToInt64(r.Values["id"])));
            var ultima = pagina.Rows[^1];
            creado = Creado(ultima);
            key = (string)ultima.Values[ServingColumns.Key]!;
            if (entrePaginas is not null) await entrePaginas();
        }
        return ids;
    }

    [Fact]
    public async Task Pagina_sin_repetir_ni_saltear()
    {
        // Las 5 filas van en un solo lote y comparten el mismo _creado: el desempate por _key es lo
        // unico que ordena entre paginas.
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 1), (2, 2), (3, 3), (4, 4), (5, 5)), Clave,
            ModoEscritura.Agregar, CancellationToken.None);

        var ids = await RecorrerAsync(store, porPagina: 2);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, ids);
    }

    [Fact]
    public async Task No_saltea_filas_si_el_pipeline_reescribe_una_mientras_se_pagina()
    {
        // Regresion del paginado por OFFSET: una corrida manual (POST api/run) puede correr en paralelo
        // con el envio. Si reescribe en Reemplazar una fila ya leida, esa fila salta al final (su
        // _creado se refresca) y todas las posiciones siguientes se corren un lugar. Con OFFSET, la
        // fila que ocupa el hueco no se lee nunca y el cursor avanza igual: se pierde sin error.
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 1), (2, 2), (3, 3), (4, 4), (5, 5)), Clave,
            ModoEscritura.Reemplazar, CancellationToken.None);

        var reescrita = false;
        var ids = await RecorrerAsync(store, porPagina: 2, entrePaginas: async () =>
        {
            if (reescrita) return;
            reescrita = true;
            await Separar();
            await store.WriteBatchAsync("t", Lote((1, 999)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);
        });

        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, ids.Distinct().OrderBy(i => i));
    }
}
