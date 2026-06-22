# DataPipeline — Fase 4A (Serving con ciclo de procesado) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. TDD donde es testeable (store + API con SQLite real + WebApplicationFactory); el editor (UI) queda build-only (consistente con Fase 3).

**Goal:** Dar al serving un ciclo de procesado: cada fila lleva columnas de control (`_key`, `_creado`, `_procesado`); el consumidor lee por GET solo lo no procesado y lo confirma (ack); cada corrida administra el ciclo segun el modo (Agregar = append + limpieza; Reemplazar = upsert por `_key`).

**Architecture:** Evoluciona `IDataStore` y `SqliteDataStore` para columnas de control + upsert/limpieza + lectura filtrada + marcar-procesadas. `PipelineDefinition` suma `ColumnasClave`. `PipelineRunner` pasa clave+modo al store. `DataController` filtra por `_procesado` y suma `POST /procesar`. El editor (3E) captura las columnas clave.

**Tech Stack:** .NET 10, EF Core 10.0.8 (Sqlite), Microsoft.Data.Sqlite, ASP.NET Core, xUnit + Mvc.Testing, Blazor (editor).

**Spec:** `docs/superpowers/specs/2026-06-21-datapipeline-fase4a-serving-procesado-design.md`.

## Global Constraints
- net10.0; Nullable/ImplicitUsings enable; build pristine (`dotnet build --no-incremental`).
- `DataPipeline.Core` sin dependencias externas (solo BCL).
- Convencion de idioma (Opcion 2): props/dominio en castellano; tipos/servicios/metodos en ingles/.NET; locales/comentarios en espanol; **sin tildes ni `ñ` en identificadores**.
- Tests con global `using Xunit`: alias `using Record = DataPipeline.Core.Records.Record;` cuando se use `Record`.
- Columnas de control reservadas: `_key`, `_creado`, `_procesado` (constantes en `ServingColumns`).
- `_key` = join de los valores de `ColumnasClave` con separador ``; si vacia y Agregar -> GUID; Reemplazar requiere `ColumnasClave` (guard en runner + store).
- Commits directo a `main`, uno por tarea, con `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Estado de partida
- `IDataStore` (Core): `EnsureTableAsync(table,columns,mode,ct)`, `WriteBatchAsync(table,batch,ct)`, `ReadPagedAsync(table,page,pageSize,ct)`.
- `SqliteDataStore` (Connectors): Append/Reemplazar(DROP+CREATE)/ReadPaged; Combinar -> NotSupported.
- `PipelineRunner`: llama `EnsureTableAsync(def.TablaDestino, lote.Columns, def.ModoEscritura, ct)` + `WriteBatchAsync(def.TablaDestino, lote, ct)`.
- `DataController.Get`: `ReadPagedAsync(def.TablaDestino, page, pageSize, ct)`.
- `ApiFactory.SeedPipelineConDatosAsync`: usa `EnsureTableAsync` + `WriteBatchAsync` (firmas viejas).
- Tests que tocan el store: `SqliteDataStoreTests`, `PipelineRunnerTests`, `PipelineRunnerTransformTests`, `EjecutorProgramadoTests`, `PullApiTests` (via ApiFactory). 52 tests verdes.

---

## File Structure
```
DataPipeline.Core/
  Pipelines/PipelineDefinition.cs    (MOD: + ColumnasClave)
  Records/ServingColumns.cs          (NEW: constantes _key/_creado/_procesado)
  Abstractions/IDataStore.cs         (MOD: nuevas firmas)
DataPipeline.Persistence/
  MetadataDbContext.cs               (MOD: converter List<string> para ColumnasClave)
  Migrations/*                       (NEW: AgregarColumnasClave)
DataPipeline.Connectors/
  Stores/SqliteDataStore.cs          (REWRITE: ciclo de procesado)
DataPipeline.Engine/
  PipelineRunner.cs                  (MOD: pasa ColumnasClave+modo; guard Reemplazar)
DataPipeline.Cli/Program.cs          (sin cambio: usa runner; no llama store directo)
DataPipeline.Api/
  Controllers/DataController.cs       (MOD: ?incluirProcesados + POST /procesar)
tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs   (MOD: roundtrip ColumnasClave)
tests/DataPipeline.Tests/SqliteDataStoreTests.cs            (REWRITE)
tests/DataPipeline.Tests/PipelineRunnerTests.cs             (MOD: ReadPaged firma)
tests/DataPipeline.Tests/PipelineRunnerTransformTests.cs    (MOD: ReadPaged firma)
tests/DataPipeline.Api.Tests/ApiFactory.cs                  (MOD: seed con nuevas firmas)
tests/DataPipeline.Api.Tests/DataProcesadoApiTests.cs       (NEW)
DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor  (MOD: Columnas clave) [build-only]
```

---

### Task 1: `PipelineDefinition.ColumnasClave` + migracion

**Files:** Modify `DataPipeline.Core/Pipelines/PipelineDefinition.cs`, `DataPipeline.Persistence/MetadataDbContext.cs`; Test `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`; migracion EF.

**Interfaces:**
- Produces: `PipelineDefinition.ColumnasClave` (`List<string>`, default `new()`).

- [ ] **Step 1: Agregar la propiedad**

En `DataPipeline.Core/Pipelines/PipelineDefinition.cs`, despues de `CorrerDespuesDe`:
```csharp
    /// <summary>Columnas de negocio que forman la clave de fila del serving (upsert + ack).</summary>
    public List<string> ColumnasClave { get; set; } = new();
```

- [ ] **Step 2: Test de roundtrip (agregar a `RepositoriosTests`, antes del `}` de la clase)**
```csharp
    [Fact]
    public async Task Upsert_roundtrip_incluye_columnas_clave()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var def = Def("conclave", null);
        def.ColumnasClave = new() { "id_venta" };
        await store.UpsertAsync(def, CancellationToken.None);

        var leido = await store.GetByNombreAsync("conclave", CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Equal(new[] { "id_venta" }, leido!.ColumnasClave.ToArray());
    }
```

- [ ] **Step 3: Compilar test -> RED**

Run: `dotnet build tests/DataPipeline.Persistence.Tests --nologo`
Expected: el test compila pero al correr fallaria porque EF no mapea `ColumnasClave` (se persiste vacia). Continuar al Step 4.

- [ ] **Step 4: Converter de `List<string>` en `MetadataDbContext`**

En `OnModelCreating`, dentro del bloque de `pipeline` (despues del converter de `CorrerDespuesDe`), agregar:
```csharp
        var clavesConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<string>>(v, jsonOpts) ?? new());
        var clavesComparer = new ValueComparer<List<string>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.ColumnasClave).HasConversion(clavesConverter).Metadata.SetValueComparer(clavesComparer);
```

- [ ] **Step 5: Migracion + verde**

Run:
```bash
dotnet ef migrations add AgregarColumnasClave --project DataPipeline.Persistence --startup-project DataPipeline.Persistence
dotnet test tests/DataPipeline.Persistence.Tests --nologo
```
Expected: PASS (roundtrip incluye `ColumnasClave`).

- [ ] **Step 6: Commit**
```bash
git add DataPipeline.Core/Pipelines/PipelineDefinition.cs DataPipeline.Persistence/MetadataDbContext.cs DataPipeline.Persistence/Migrations/ tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs
git commit -m "feat(core,persistence): PipelineDefinition.ColumnasClave + migracion

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Ciclo de procesado en el store (contrato + SqliteDataStore + call-sites)

**Files:** Create `DataPipeline.Core/Records/ServingColumns.cs`; Modify `DataPipeline.Core/Abstractions/IDataStore.cs`, `DataPipeline.Connectors/Stores/SqliteDataStore.cs`, `DataPipeline.Engine/PipelineRunner.cs`, `DataPipeline.Api/Controllers/DataController.cs`, `tests/DataPipeline.Api.Tests/ApiFactory.cs`; Rewrite `tests/DataPipeline.Tests/SqliteDataStoreTests.cs`; Modify `tests/DataPipeline.Tests/PipelineRunnerTests.cs`, `tests/DataPipeline.Tests/PipelineRunnerTransformTests.cs`.

**Interfaces:**
- Produces:
  - `static class ServingColumns { const string Key="_key"; const string Creado="_creado"; const string Procesado="_procesado"; }`
  - `IDataStore`:
    - `Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct)`
    - `Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct)`
    - `Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct)`
    - `Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct)`

- [ ] **Step 1: `ServingColumns` (Core)**

`DataPipeline.Core/Records/ServingColumns.cs`:
```csharp
namespace DataPipeline.Core.Records;

/// <summary>Nombres reservados de las columnas de control del serving.</summary>
public static class ServingColumns
{
    public const string Key = "_key";
    public const string Creado = "_creado";
    public const string Procesado = "_procesado";
}
```

- [ ] **Step 2: Nuevo contrato `IDataStore`**

Reemplazar `DataPipeline.Core/Abstractions/IDataStore.cs` por:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Sink interno + lectura/ack para la entrega PULL con ciclo de procesado.</summary>
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct);
    Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct);
}
```

- [ ] **Step 3: Reescribir `SqliteDataStore`**

Reemplazar `DataPipeline.Connectors/Stores/SqliteDataStore.cs` por:
```csharp
using System.Globalization;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Microsoft.Data.Sqlite;

namespace DataPipeline.Connectors.Stores;

/// <summary>
/// IDataStore sobre SQLite con ciclo de procesado. Cada fila lleva _key/_creado/_procesado.
/// Agregar = append + limpieza de _procesado=1; Reemplazar = upsert por _key (sin borrar ausentes).
/// </summary>
public sealed class SqliteDataStore : IDataStore
{
    private readonly string _connectionString;
    public SqliteDataStore(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct)
    {
        if (mode == ModoEscritura.Combinar)
            throw new NotSupportedException("ModoEscritura.Combinar no aplica al serving con ciclo de procesado.");
        if (columnasNegocio.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columnasNegocio));

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var todas = columnasNegocio
            .Concat(new[] { ServingColumns.Key, ServingColumns.Creado, ServingColumns.Procesado })
            .Select(Quote);
        await using (var crear = con.CreateCommand())
        {
            crear.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({string.Join(", ", todas)});";
            await crear.ExecuteNonQueryAsync(ct);
        }

        if (mode == ModoEscritura.Reemplazar)
        {
            await using var idx = con.CreateCommand();
            idx.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {Quote("ux_" + table + "_key")} " +
                              $"ON {Quote(table)} ({Quote(ServingColumns.Key)});";
            await idx.ExecuteNonQueryAsync(ct);
        }
        else // Agregar: limpieza de filas ya procesadas
        {
            await using var del = con.CreateCommand();
            del.CommandText = $"DELETE FROM {Quote(table)} WHERE {Quote(ServingColumns.Procesado)} = 1;";
            await del.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct)
    {
        if (batch.Rows.Count == 0) return;
        if (mode == ModoEscritura.Reemplazar && columnasClave.Count == 0)
            throw new InvalidOperationException("Reemplazar (upsert) requiere al menos una columna clave.");

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await con.BeginTransactionAsync(ct);

        var negocio = batch.Columns;
        var todas = negocio.Concat(new[] { ServingColumns.Key, ServingColumns.Creado, ServingColumns.Procesado }).ToArray();
        var listaCols = string.Join(", ", todas.Select(Quote));
        var listaParams = string.Join(", ", todas.Select((_, i) => "$p" + i));

        var sql = $"INSERT INTO {Quote(table)} ({listaCols}) VALUES ({listaParams})";
        if (mode == ModoEscritura.Reemplazar)
        {
            var sets = negocio.Select(c => $"{Quote(c)} = excluded.{Quote(c)}")
                .Concat(new[]
                {
                    $"{Quote(ServingColumns.Creado)} = excluded.{Quote(ServingColumns.Creado)}",
                    $"{Quote(ServingColumns.Procesado)} = 0"
                });
            sql += $" ON CONFLICT({Quote(ServingColumns.Key)}) DO UPDATE SET {string.Join(", ", sets)}";
        }
        sql += ";";

        await using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var parms = new SqliteParameter[todas.Length];
        for (int i = 0; i < todas.Length; i++)
        {
            parms[i] = cmd.CreateParameter();
            parms[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(parms[i]);
        }

        int idxKey = negocio.Count, idxCreado = negocio.Count + 1, idxProc = negocio.Count + 2;
        var ahora = DateTime.Now;
        foreach (var fila in batch.Rows)
        {
            for (int i = 0; i < negocio.Count; i++)
            {
                fila.Values.TryGetValue(negocio[i], out var valor);
                parms[i].Value = valor ?? DBNull.Value;
            }
            parms[idxKey].Value = CalcularKey(fila, columnasClave);
            parms[idxCreado].Value = ahora;
            parms[idxProc].Value = 0;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var filtro = incluirProcesados ? "" : $" WHERE {Quote(ServingColumns.Procesado)} = 0";
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)}{filtro} ORDER BY {Quote(ServingColumns.Creado)} " +
                          "LIMIT $take OFFSET $skip;";
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "$take"; pTake.Value = pageSize; cmd.Parameters.Add(pTake);
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "$skip"; pSkip.Value = (page - 1) * pageSize; cmd.Parameters.Add(pSkip);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();
        var filas = new List<Record>();
        while (await rd.ReadAsync(ct))
        {
            var vals = new Dictionary<string, object?>(cols.Length);
            for (int i = 0; i < cols.Length; i++)
                vals[cols[i]] = await rd.IsDBNullAsync(i, ct) ? null : rd.GetValue(i);
            filas.Add(new Record { Values = vals });
        }
        return new RecordBatch { Columns = cols, Rows = filas };
    }

    public async Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return 0;
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        var ph = string.Join(", ", keys.Select((_, i) => "$k" + i));
        cmd.CommandText = $"UPDATE {Quote(table)} SET {Quote(ServingColumns.Procesado)} = 1 " +
                          $"WHERE {Quote(ServingColumns.Key)} IN ({ph});";
        for (int i = 0; i < keys.Count; i++)
        {
            var p = cmd.CreateParameter(); p.ParameterName = "$k" + i; p.Value = keys[i]; cmd.Parameters.Add(p);
        }
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string CalcularKey(Record fila, IReadOnlyList<string> claves)
    {
        if (claves.Count == 0) return Guid.NewGuid().ToString("N");
        var partes = claves.Select(c => fila.Values.TryGetValue(c, out var v)
            ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "");
        return string.Join('', partes);
    }

    private static string Quote(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
```

- [ ] **Step 4: Adaptar `PipelineRunner`** (pasar clave+modo; guard Reemplazar)

En `DataPipeline.Engine/PipelineRunner.cs`, reemplazar el bloque de escritura del `await foreach`:
```csharp
            var tablaLista = false;
            await foreach (var lote in stream.WithCancellation(ct))
            {
                if (lote.Rows.Count == 0) continue;

                if (!tablaLista)
                {
                    await _dataStore.EnsureTableAsync(def.TablaDestino, lote.Columns, def.ModoEscritura, ct);
                    tablaLista = true;
                }
                await _dataStore.WriteBatchAsync(def.TablaDestino, lote, def.ColumnasClave, def.ModoEscritura, ct);
                escritas += lote.Rows.Count;
            }
```
(El guard de Reemplazar+sin-clave ya lo lanza `WriteBatchAsync`; queda capturado por el try/catch y devuelve `RunResult.Success=false`.)

- [ ] **Step 5: Adaptar `DataController.Get`** (firma de ReadPaged; comportamiento por defecto: solo no procesadas)

En `DataPipeline.Api/Controllers/DataController.cs`, cambiar la llamada:
```csharp
            var lote = await _data.ReadPagedAsync(def.TablaDestino, page, pageSize, false, ct);
```

- [ ] **Step 6: Adaptar `ApiFactory.SeedPipelineConDatosAsync`** (nuevas firmas)

En `tests/DataPipeline.Api.Tests/ApiFactory.cs`, reemplazar las dos llamadas al store:
```csharp
        await data.EnsureTableAsync(tabla, new[] { "importe" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await data.WriteBatchAsync(tabla, new RecordBatch
        {
            Columns = new[] { "importe" },
            Rows = new[] { new Record { Values = new Dictionary<string, object?> { ["importe"] = 42 } } }
        }, new[] { "importe" }, ModoEscritura.Reemplazar, CancellationToken.None);
```

- [ ] **Step 7: Reescribir `SqliteDataStoreTests`**

Reemplazar `tests/DataPipeline.Tests/SqliteDataStoreTests.cs` por:
```csharp
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
        var cs = NuevaDb();
        var store = new SqliteDataStore(cs);
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
```

- [ ] **Step 8: Adaptar `PipelineRunnerTests` y `PipelineRunnerTransformTests`** (firma de ReadPaged)

En ambos archivos, donde haya `ReadPagedAsync("destino", 1, 10, CancellationToken.None)`, cambiar por `ReadPagedAsync("destino", 1, 10, true, CancellationToken.None)` (el `true` = incluir procesadas, para contar todas las filas escritas). En `PipelineRunnerTransformTests`, la def usa `ModoEscritura.Reemplazar` con un solo paso source sin columnas clave: **agregar** `ColumnasClave = { "importe" }` a la `PipelineDefinition` del test (porque Reemplazar ahora exige clave). En `PipelineRunnerTests`, `DefMinima()` usa `ModoEscritura.Agregar` (no requiere clave) — solo cambiar la firma de ReadPaged.

- [ ] **Step 9: Build pristine + suite completa**

Run: `dotnet build --no-incremental --nologo` -> 0 warnings.
Run: `dotnet test --nologo` -> PASS (store reescrito + runner/api/ejecutor adaptados).

- [ ] **Step 10: Commit**
```bash
git add -A
git commit -m "feat(core,connectors,engine,api): serving con ciclo de procesado (control cols + upsert + limpieza)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: API — `?incluirProcesados` + `POST /api/data/{nombre}/procesar`

**Files:** Modify `DataPipeline.Api/Controllers/DataController.cs`; Test `tests/DataPipeline.Api.Tests/DataProcesadoApiTests.cs` (NEW).

**Interfaces:**
- Consumes: `IPipelineStore`, `IDataStore.ReadPagedAsync/MarcarProcesadasAsync`, `ServingColumns`, `ApiFactory.SeedPipelineConDatosAsync`/`CredPull`.
- Produces:
  - `GET /api/data/{nombre}?incluirProcesados=true` -> incluye filas procesadas.
  - `POST /api/data/{nombre}/procesar` (rol pull) body `{ "keys": ["..."] }` -> `200 { marcadas }`.

- [ ] **Step 1: Test (NEW `DataProcesadoApiTests`)**
```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataProcesadoApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DataProcesadoApiTests(ApiFactory factory) => _factory = factory;

    private sealed record Pagina(List<string> Columns, List<Dictionary<string, object>> Rows);
    private sealed record AckResp(int Marcadas);

    [Fact]
    public async Task Procesar_marca_y_desaparece_del_get_por_defecto()
    {
        await _factory.SeedPipelineConDatosAsync("ventasp", "serving_ventasp");
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();

        // GET inicial: 1 fila no procesada; tomamos su _key
        var pagina = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?page=1&pageSize=10");
        Assert.NotNull(pagina);
        Assert.Single(pagina!.Rows);
        var key = pagina.Rows[0]["_key"].ToString()!;

        // Ack
        var ack = await c.PostAsJsonAsync("/api/data/ventasp/procesar", new { keys = new[] { key } });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        Assert.Equal(1, (await ack.Content.ReadFromJsonAsync<AckResp>())!.Marcadas);

        // GET por defecto: ya no aparece
        var vacia = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?page=1&pageSize=10");
        Assert.Empty(vacia!.Rows);

        // GET incluirProcesados: vuelve a aparecer
        var todas = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?incluirProcesados=true");
        Assert.Single(todas!.Rows);
    }

    [Fact]
    public async Task Procesar_sin_auth_401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/data/x/procesar", new { keys = new[] { "k" } });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
```

- [ ] **Step 2: RED**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataProcesadoApiTests" --nologo`
Expected: FALLA (no existe el endpoint /procesar ni el parametro incluirProcesados).

- [ ] **Step 3: Implementar en `DataController`**

Reemplazar el cuerpo de `DataController` (metodos) por:
```csharp
    [HttpGet("{nombre}")]
    public async Task<IActionResult> Get(string nombre, int page = 1, int pageSize = 100,
        bool incluirProcesados = false, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1) return BadRequest("page y pageSize deben ser >= 1.");

        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");

        try
        {
            var lote = await _data.ReadPagedAsync(def.TablaDestino, page, pageSize, incluirProcesados, ct);
            return Ok(new { columns = lote.Columns, rows = lote.Rows.Select(r => r.Values) });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aun no existe (el pipeline no corrio todavia).");
        }
    }

    public sealed record ProcesarRequest(List<string> Keys);

    [HttpPost("{nombre}/procesar")]
    public async Task<IActionResult> Procesar(string nombre, [FromBody] ProcesarRequest req, CancellationToken ct)
    {
        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");
        if (req?.Keys is null || req.Keys.Count == 0) return Ok(new { marcadas = 0 });

        try
        {
            var n = await _data.MarcarProcesadasAsync(def.TablaDestino, req.Keys, ct);
            return Ok(new { marcadas = n });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aun no existe.");
        }
    }
```

- [ ] **Step 4: GREEN + build pristine + suite completa**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataProcesadoApiTests" --nologo` -> PASS.
Run: `dotnet build --no-incremental --nologo` -> 0 warnings; `dotnet test --nologo` -> PASS (todo).

- [ ] **Step 5: Commit**
```bash
git add DataPipeline.Api/Controllers/DataController.cs tests/DataPipeline.Api.Tests/DataProcesadoApiTests.cs
git commit -m "feat(api): GET ?incluirProcesados + POST /api/data/{nombre}/procesar (ack)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Editor (3E) — input "Columnas clave" (build-only)

**Files:** Modify `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor`.

**Interfaces:**
- Consumes: `KeyValueEditor` no aplica (es lista simple). Se usa una lista de strings con agregar/quitar, o un `SfTextBox` con valores separados por coma.
- Produces: `PipelineForm.ColumnasClave` -> `PipelineDefinition.ColumnasClave`; parseo al cargar.

- [ ] **Step 1: Agregar el campo al form model y la UI**

En `Editor.razor`, en `PipelineForm` agregar:
```csharp
        public string ColumnasClave { get; set; } = ""; // nombres separados por coma
```
En la seccion "Generales" (despues de Tabla destino), agregar:
```razor
            <div class="mb-3">
                <SfTextBox Placeholder="Columnas clave (separadas por coma; requerido si Reemplazar)"
                           FloatLabelType="FloatLabelType.Always" @bind-Value="_form.ColumnasClave" />
            </div>
```

- [ ] **Step 2: Construir y parsear**

En `ConstruirDef()`, al crear el `PipelineDefinition`, agregar:
```csharp
            ColumnasClave = _form.ColumnasClave
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
```
En `Cargar(def)`, agregar:
```csharp
        _form.ColumnasClave = string.Join(", ", def.ColumnasClave);
```
En `Guardar()`, agregar validacion antes de construir (despues de las validaciones existentes):
```csharp
        if (_form.ModoEscritura == 0 /* Agregar */ == false && _form.ModoEscritura == 2 /* Reemplazar */
            && string.IsNullOrWhiteSpace(_form.ColumnasClave))
        {
            _error = "Reemplazar requiere al menos una columna clave.";
            return;
        }
```
> Simplificar: la condicion correcta es `if (_form.ModoEscritura == 2 && string.IsNullOrWhiteSpace(_form.ColumnasClave))`.

- [ ] **Step 3: Build pristine + commit**

Run: `dotnet build --no-incremental --nologo` -> 0 warnings.
```bash
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "feat(admin): editor captura ColumnasClave (requerido en Reemplazar) [build-only]

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage:**
- Columnas de control `_key/_creado/_procesado` -> Task 2 (ServingColumns + store) ✅
- `ColumnasClave` configurable -> Task 1 (modelo) + Task 4 (editor) ✅
- Agregar = append + limpieza de procesadas -> Task 2 (EnsureTable delete + WriteBatch insert) ✅
- Reemplazar = upsert por _key, sin borrar ausentes -> Task 2 (ON CONFLICT) ✅
- GET solo no procesadas + `?incluirProcesados` -> Task 2 (default false) + Task 3 (param) ✅
- `POST /procesar` ack por keys -> Task 3 ✅
- Testing TDD del store + API; editor build-only -> Tasks 2/3 (tests) + Task 4 (build-only) ✅

**2. Placeholder scan:** sin TBD/TODO. La condicion de validacion del editor se aclara explicitamente (`ModoEscritura == 2`).

**3. Type consistency:** `IDataStore` nuevas firmas usadas igual en `SqliteDataStore`, `PipelineRunner`, `DataController`, `ApiFactory` y tests; `ServingColumns.Key/Creado/Procesado` usados en store, tests y test de API; `PipelineDefinition.ColumnasClave` (List<string>) definido en Task 1 y consumido en Task 2 (runner) y Task 4 (editor); `_key` calculado en el store y devuelto por GET / usado en `/procesar`.

**4. Notas de alcance:** Reemplazar exige `ColumnasClave` (guard en store + validacion en editor). Agregar sin clave usa surrogate GUID. `Combinar` sigue NotSupported. SqlServerDataStore configurable: diferido.
