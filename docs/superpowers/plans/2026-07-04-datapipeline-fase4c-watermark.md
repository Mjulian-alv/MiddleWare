# Fase 4C — Carga incremental (watermark) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que un pipeline traiga solo lo nuevo del origen desde la ultima corrida, trackeando el maximo de una columna monotona (watermark) y persistiendolo tras cada corrida exitosa.

**Architecture:** `PipelineDefinition` suma `ColumnaWatermark` (que columna del origen trackear) y `UltimoWatermark` (ultimo maximo visto). El SQL del usuario referencia `@watermark`; el `PipelineRunner` inyecta ese parametro (DBNull si nulo), trackea el MAX de la columna en la etapa del origen (antes de transforms) y lo devuelve en `RunResult.NuevoWatermark`. Los orquestadores (`EjecutorProgramado`, `RunsController`) persisten el watermark via `IPipelineStore.ActualizarWatermarkAsync` solo si la corrida fue exitosa.

**Tech Stack:** .NET 10, C#, EF Core (SQLite, almacen A), xUnit. Blazor Server + Syncfusion (editor).

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches ni remoto. **NO usar `git add -A`**: stagear rutas explicitas (una vez colo la clave Syncfusion en un commit).
- Gate de 0 warnings: verificar con `dotnet build --no-incremental`.
- El watermark se guarda como **string**. Formato: `DateTime` en ISO 8601 round-trip (`"O"`); otros tipos con `Convert.ToString(InvariantCulture)`.
- El pipeline NO reescribe el SQL: el usuario controla el operador (`>` vs `>=`) y el casteo en su `@watermark IS NULL OR col > @watermark`.
- No cambia el contrato de `ISource`/`IDataStore` (reusa `ctx.Parameters`, que `SqlServerSource` ya bindea).

---

### Task 1: Modelo de datos — `ColumnaWatermark` + `UltimoWatermark` + migracion

**Files:**
- Modify: `DataPipeline.Core/Pipelines/PipelineDefinition.cs`
- Create: `DataPipeline.Persistence/Migrations/<timestamp>_AgregarWatermark.cs` (lo genera `dotnet ef`)
- Test: `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`

**Interfaces:**
- Produces: `PipelineDefinition.ColumnaWatermark` (`string?`), `PipelineDefinition.UltimoWatermark` (`string?`).

- [ ] **Step 1: Agregar las dos propiedades a `PipelineDefinition`**

En `PipelineDefinition.cs`, despues de `ColumnasClave` (linea 24) y antes de `FilasPorLote`:

```csharp
    /// <summary>Columna del origen cuyo maximo se trackea para carga incremental. Vacio = no incremental.</summary>
    public string? ColumnaWatermark { get; set; }

    /// <summary>Ultimo maximo visto de <see cref="ColumnaWatermark"/> (persistido tras cada corrida exitosa).</summary>
    public string? UltimoWatermark { get; set; }
```

Nota: son `string?` simples; el mapeo EF por defecto los toma como columnas TEXT nullable. NO hace falta value converter (a diferencia de `ColumnasClave`, que es `List<string>`).

- [ ] **Step 2: Escribir el test de persistencia (falla)**

En `RepositoriosTests.cs`, agregar un test que guarde un pipeline con watermark, lo relea y verifique ambos campos. Seguir el patron de los tests existentes en ese archivo para instanciar el store/context. Ejemplo del cuerpo:

```csharp
[Fact]
public async Task Pipeline_persiste_columna_y_ultimo_watermark()
{
    await using var ctx = NuevoContexto(); // helper existente del archivo
    var store = new EfPipelineStore(ctx);
    var def = new PipelineDefinition
    {
        Id = Guid.NewGuid(),
        Nombre = "ventas-inc",
        TablaDestino = "ventas",
        ColumnaWatermark = "fecha_mod",
        UltimoWatermark = "2026-06-20T18:00:00.0000000"
    };
    await store.UpsertAsync(def, CancellationToken.None);

    var leido = await store.GetByIdAsync(def.Id, CancellationToken.None);

    Assert.NotNull(leido);
    Assert.Equal("fecha_mod", leido!.ColumnaWatermark);
    Assert.Equal("2026-06-20T18:00:00.0000000", leido.UltimoWatermark);
}
```

> Antes de escribir, abrir `RepositoriosTests.cs` y usar el mismo helper de creacion de contexto (p. ej. `NuevoContexto()`/`NuevaDb()`) que ya usan los tests de pipelines de ese archivo. Reemplazar el nombre del helper si difiere.

- [ ] **Step 3: Correr el test — debe fallar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: FAIL — la migracion aun no existe, la tabla no tiene las columnas (error de SQLite "no such column" o similar).

- [ ] **Step 4: Generar la migracion EF**

Run:
```bash
dotnet ef migrations add AgregarWatermark --project DataPipeline.Persistence --startup-project DataPipeline.Persistence
```
Esto usa el `MetadataDbContextFactory` existente y crea el archivo de migracion + actualiza el snapshot. Verificar que el `Up` agrega dos columnas `ColumnaWatermark` y `UltimoWatermark` (TEXT, nullable: true).

- [ ] **Step 5: Correr el test — debe pasar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Core/Pipelines/PipelineDefinition.cs DataPipeline.Persistence/Migrations tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs
git commit -m "feat(core,persistence): PipelineDefinition.ColumnaWatermark + UltimoWatermark + migracion"
```

---

### Task 2: `RunResult.NuevoWatermark` + tracking del maximo en el runner

**Files:**
- Modify: `DataPipeline.Engine/RunResult.cs`
- Modify: `DataPipeline.Engine/PipelineRunner.cs`
- Test: `tests/DataPipeline.Tests/PipelineRunnerTests.cs`

**Interfaces:**
- Consumes: `PipelineDefinition.ColumnaWatermark`, `UltimoWatermark` (Task 1); `RunContext.Parameters` (`IReadOnlyDictionary<string,string>`); `FakeSource`, `FakeSourceFactory` (`tests/DataPipeline.Tests/Fakes/FakeSource.cs`); `Record.Values` (`IReadOnlyDictionary<string, object?>`).
- Produces: `RunResult.NuevoWatermark` (`string?`, ultimo posicional del record). El runner inyecta `ctx.Parameters["watermark"]` cuando `ColumnaWatermark` esta seteada.

- [ ] **Step 1: Agregar `NuevoWatermark` al record `RunResult`**

En `RunResult.cs`, sumar el parametro posicional al final:

```csharp
namespace DataPipeline.Engine;

/// <summary>Resultado de una corrida de pipeline.</summary>
public sealed record RunResult(
    Guid RunId,
    bool Success,
    long RowsRead,
    long RowsWritten,
    string? Error,
    TimeSpan Duration,
    string? NuevoWatermark = null);
```

Con default `= null`, los `new RunResult(...)` existentes en `PipelineRunner` siguen compilando; se actualizan en el Step siguiente.

- [ ] **Step 2: Escribir el test de tracking + inyeccion (falla)**

En `PipelineRunnerTests.cs` agregar dos tests. Primero, un `FakeSource` que capture los parametros recibidos (ver Step 3 de esta task para el helper). Test A — trackea el maximo:

```csharp
[Fact]
public async Task Trackea_el_maximo_de_la_columna_watermark()
{
    var cs = NuevaDb();
    var store = new SqliteDataStore(cs);
    var filas = new[]
    {
        new Record { Values = new Dictionary<string, object?> { ["id"] = 1, ["fecha_mod"] = new DateTime(2026, 6, 20, 18, 0, 0) } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 2, ["fecha_mod"] = new DateTime(2026, 6, 21, 10, 30, 0) } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 3, ["fecha_mod"] = (object?)null } },
    };
    var source = new FakeSource(new[] { "id", "fecha_mod" }, filas, porLote: 2);
    var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

    var def = DefMinima();
    def.ColumnaWatermark = "fecha_mod";

    var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

    Assert.True(result.Success, result.Error);
    Assert.Equal(new DateTime(2026, 6, 21, 10, 30, 0).ToString("O"), result.NuevoWatermark);
}
```

Test B — inyecta el watermark previo en los parametros del source:

```csharp
[Fact]
public async Task Inyecta_UltimoWatermark_en_los_parametros_del_origen()
{
    var store = new SqliteDataStore(NuevaDb());
    var spy = new SpyParamsSource(new[] { "id", "fecha_mod" });
    var runner = new PipelineRunner(new FakeSourceFactory(spy), new TransformFactory(), store);

    var def = DefMinima();
    def.ColumnaWatermark = "fecha_mod";
    def.UltimoWatermark = "2026-06-20T18:00:00.0000000";

    await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

    Assert.True(spy.ParametrosVistos.ContainsKey("watermark"));
    Assert.Equal("2026-06-20T18:00:00.0000000", spy.ParametrosVistos["watermark"]);
}
```

- [ ] **Step 3: Agregar el `SpyParamsSource` a los fakes**

En `tests/DataPipeline.Tests/Fakes/FakeSource.cs`, agregar un source que registra los parametros del `RunContext`:

```csharp
/// <summary>Source de prueba que registra los parametros recibidos (para verificar la inyeccion del watermark).</summary>
internal sealed class SpyParamsSource : ISource
{
    private readonly IReadOnlyList<string> _cols;
    public IReadOnlyDictionary<string, string> ParametrosVistos { get; private set; } =
        new Dictionary<string, string>();

    public SpyParamsSource(IReadOnlyList<string> cols) => _cols = cols;

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        ParametrosVistos = ctx.Parameters;
        await Task.Yield();
        yield return new RecordBatch { Columns = _cols, Rows = Array.Empty<Record>() };
    }
}
```

- [ ] **Step 4: Correr los tests — deben fallar**

Run: `dotnet test tests/DataPipeline.Tests --nologo`
Expected: FAIL — `NuevoWatermark` siempre null (no se trackea) y `watermark` no aparece en los parametros.

- [ ] **Step 5: Implementar el tracking + inyeccion en `PipelineRunner`**

En `PipelineRunner.RunAsync`, dentro del `try`:

a) Construir los parametros efectivos (inyectar `watermark` si corresponde) ANTES de armar el `ctx`. Justo antes del `var ctx = new RunContext { ... }`, agregar:

```csharp
            // Carga incremental: si hay watermark previo, inyectarlo como parametro del origen.
            // El SQL del usuario lo referencia como @watermark. Solo se agrega cuando hay valor:
            // en la primera corrida (UltimoWatermark null) SqlServerSource declara @watermark=NULL
            // por su cuenta (ver Task 3) para que "@watermark IS NULL OR ..." traiga todo.
            var parametrosEfectivos = parameters;
            if (!string.IsNullOrWhiteSpace(def.ColumnaWatermark) && def.UltimoWatermark is not null)
            {
                parametrosEfectivos = new Dictionary<string, string>(parameters)
                {
                    ["watermark"] = def.UltimoWatermark
                };
            }
```

Y usar `Parameters = parametrosEfectivos` (en vez de `Parameters = parameters`) en el `new RunContext { ... }`.

> **Por que solo se agrega la clave cuando hay valor:** `ctx.Parameters` es `IReadOnlyDictionary<string,string>` (valor NON-nullable), asi que no se puede almacenar `null`. En la primera corrida (o tras reset) `UltimoWatermark` es null y la clave `watermark` NO se agrega; la declaracion de `@watermark=NULL` la hace `SqlServerSource` (Task 3) para que SQL Server no exija la variable.

b) Trackear el maximo en la etapa del origen. Agregar una variable local y envolver el stream del source (junto a `ContarLeidas`, que ya cuenta filas antes de transforms). Declarar antes de los helpers locales:

```csharp
            object? maxWatermark = null;
```

Modificar `ContarLeidas` para que tambien trackee el maximo de la columna (asi se hace en la etapa del origen, con el nombre ORIGINAL de la columna, antes de cualquier rename de transform):

```csharp
            // Cuenta filas leidas y trackea el maximo de la columna watermark (antes de transforms).
            async IAsyncEnumerable<RecordBatch> ContarLeidas(IAsyncEnumerable<RecordBatch> src)
            {
                await foreach (var b in src.WithCancellation(ct))
                {
                    leidas += b.Rows.Count;
                    if (!string.IsNullOrWhiteSpace(def.ColumnaWatermark))
                    {
                        foreach (var fila in b.Rows)
                        {
                            if (fila.Values.TryGetValue(def.ColumnaWatermark, out var v) && v is not null)
                            {
                                if (maxWatermark is null || Comparer<object>.Default.Compare(v, maxWatermark) > 0)
                                    maxWatermark = v;
                            }
                        }
                    }
                    yield return b;
                }
            }
```

c) Formatear el maximo a string y devolverlo en el `RunResult` exitoso. Reemplazar el `return new RunResult(runId, true, leidas, escritas, null, cronometro.Elapsed);` por:

```csharp
            cronometro.Stop();
            var nuevoWatermark = maxWatermark switch
            {
                null => null,
                DateTime dt => dt.ToString("O"),
                _ => Convert.ToString(maxWatermark, System.Globalization.CultureInfo.InvariantCulture)
            };
            return new RunResult(runId, true, leidas, escritas, null, cronometro.Elapsed, nuevoWatermark);
```

El `return` del `catch` queda igual (sin watermark; el default `null` aplica).

- [ ] **Step 6: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --nologo`
Expected: PASS (incluidos los dos nuevos y los 58 previos).

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Engine/RunResult.cs DataPipeline.Engine/PipelineRunner.cs tests/DataPipeline.Tests/PipelineRunnerTests.cs tests/DataPipeline.Tests/Fakes/FakeSource.cs
git commit -m "feat(engine): RunResult.NuevoWatermark + tracking del maximo e inyeccion de @watermark en el runner"
```

---

### Task 3: Binding incondicional de `@watermark` en `SqlServerSource` (build-only)

**Files:**
- Modify: `DataPipeline.Connectors/Sources/SqlServerSource.cs`

**Interfaces:**
- Consumes: `RunContext.Parameters`.
- Produces: cuando el SQL referencia `@watermark` y no vino en `ctx.Parameters`, el source igual declara `@watermark = DBNull` para que SQL Server no falle en la primera corrida.

> **Por que build-only:** el binding real solo se puede probar contra SQL Server. No hay test automatizado; se valida manualmente. El cambio es defensivo y minimo.

- [ ] **Step 1: Declarar `@watermark` como DBNull si el SQL lo referencia y no vino en parametros**

En `SqlServerSource.ReadAsync`, despues del `foreach` que agrega los parametros de `ctx.Parameters` (linea 30-31), agregar:

```csharp
        // Carga incremental: si el SQL usa @watermark pero no vino en los parametros (primera corrida /
        // reset), declararlo como NULL para que SQL Server no exija la variable. El usuario filtra con
        // "@watermark IS NULL OR col > @watermark", asi que NULL trae todo.
        if (!cmd.Parameters.Contains("@watermark")
            && _sql.Contains("@watermark", StringComparison.OrdinalIgnoreCase))
        {
            cmd.Parameters.AddWithValue("@watermark", DBNull.Value);
        }
```

- [ ] **Step 2: Verificar que compila sin warnings**

Run: `dotnet build DataPipeline.Connectors --no-incremental --nologo`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add DataPipeline.Connectors/Sources/SqlServerSource.cs
git commit -m "feat(connectors): SqlServerSource declara @watermark=NULL si el SQL lo usa y no vino (primera corrida)"
```

---

### Task 4: `IPipelineStore.ActualizarWatermarkAsync` + implementacion EF

**Files:**
- Modify: `DataPipeline.Core/Abstractions/IPipelineStore.cs`
- Modify: `DataPipeline.Persistence/EfPipelineStore.cs`
- Test: `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`

**Interfaces:**
- Produces: `Task IPipelineStore.ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct)` — persiste `UltimoWatermark` del pipeline `id`.

- [ ] **Step 1: Agregar el metodo a la interfaz**

En `IPipelineStore.cs`, despues de `ActualizarProgramacionAsync` (linea 13):

```csharp
    Task ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct);
```

- [ ] **Step 2: Escribir el test (falla)**

En `RepositoriosTests.cs`:

```csharp
[Fact]
public async Task ActualizarWatermark_persiste_el_ultimo_valor()
{
    await using var ctx = NuevoContexto(); // helper existente
    var store = new EfPipelineStore(ctx);
    var def = new PipelineDefinition
    {
        Id = Guid.NewGuid(),
        Nombre = "ventas-inc2",
        TablaDestino = "ventas",
        ColumnaWatermark = "fecha_mod"
    };
    await store.UpsertAsync(def, CancellationToken.None);

    await store.ActualizarWatermarkAsync(def.Id, "2026-06-21T10:30:00.0000000", CancellationToken.None);

    var leido = await store.GetByIdAsync(def.Id, CancellationToken.None);
    Assert.Equal("2026-06-21T10:30:00.0000000", leido!.UltimoWatermark);
}
```

- [ ] **Step 3: Correr el test — debe fallar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: FAIL — `EfPipelineStore` no implementa el metodo (error de compilacion: la interfaz exige el miembro).

- [ ] **Step 4: Implementar en `EfPipelineStore`**

En `EfPipelineStore.cs`, despues de `ActualizarProgramacionAsync` (linea 41), siguiendo el mismo patron:

```csharp
    public async Task ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return;
        def.UltimoWatermark = watermark;
        await _db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 5: Correr el test — debe pasar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Core/Abstractions/IPipelineStore.cs DataPipeline.Persistence/EfPipelineStore.cs tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs
git commit -m "feat(core,persistence): IPipelineStore.ActualizarWatermarkAsync"
```

---

### Task 5: `EjecutorProgramado` persiste el watermark tras corrida exitosa

**Files:**
- Modify: `DataPipeline.Engine/EjecutorProgramado.cs`
- Test: `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`

**Interfaces:**
- Consumes: `IPipelineStore.ActualizarWatermarkAsync` (Task 4); `RunResult.Success`, `RunResult.NuevoWatermark` (Task 2).

- [ ] **Step 1: Escribir el test — persiste si exito y hay watermark (falla)**

En `EjecutorProgramadoTests.cs`, seguir el patron existente (fakes de `IPipelineStore`/`IHistorialCorridas`/`PipelineHydrator` ya usados en el archivo). Agregar dos tests. Test A — persiste tras exito:

```csharp
[Fact]
public async Task Persiste_el_watermark_tras_corrida_exitosa()
{
    // Arrange: un pipeline vencido con ColumnaWatermark, un runner (real o fake) que
    // devuelve RunResult.Success=true con NuevoWatermark no nulo, y un store fake que
    // registra las llamadas a ActualizarWatermarkAsync.
    // (Reusar los helpers/fakes que ya arma este archivo de tests.)
    var store = new StoreSpy(vencidos: new[] { PipelineConWatermark() });
    var ejecutor = ConstruirEjecutor(store, resultado: OkConWatermark("2026-06-21T10:30:00.0000000"));

    await ejecutor.ProcesarVencidosAsync(DateTime.UtcNow, CancellationToken.None);

    Assert.Equal("2026-06-21T10:30:00.0000000", store.UltimoWatermarkPersistido);
}
```

Test B — NO persiste si la corrida fallo:

```csharp
[Fact]
public async Task No_persiste_el_watermark_si_la_corrida_fallo()
{
    var store = new StoreSpy(vencidos: new[] { PipelineConWatermark() });
    var ejecutor = ConstruirEjecutor(store, resultado: FalloConWatermark("2026-06-21T10:30:00.0000000"));

    await ejecutor.ProcesarVencidosAsync(DateTime.UtcNow, CancellationToken.None);

    Assert.False(store.SeLlamoActualizarWatermark);
}
```

> Los nombres `StoreSpy`, `ConstruirEjecutor`, `OkConWatermark`, etc. son placeholders: usar los fakes/helpers que YA existen en `EjecutorProgramadoTests.cs`. Abrir el archivo primero. Si el store fake existente no expone `ActualizarWatermarkAsync`, agregar la implementacion (registrar el valor recibido y un flag de si se llamo). Si el runner es real (`PipelineRunner` con `FakeSource`), armar el `FakeSource` con una columna watermark para que produzca un `NuevoWatermark` real, y para el caso de fallo, hacer que la corrida falle (p. ej. pipeline sin Origen).

- [ ] **Step 2: Correr los tests — deben fallar**

Run: `dotnet test tests/DataPipeline.Tests --nologo`
Expected: FAIL — el ejecutor no llama a `ActualizarWatermarkAsync`.

- [ ] **Step 3: Implementar la persistencia en `EjecutorProgramado`**

En `ProcesarVencidosAsync`, dentro del `try`, despues de `CompletarAsync` (linea 37):

```csharp
                if (result.Success && result.NuevoWatermark is not null)
                    await _store.ActualizarWatermarkAsync(def.Id, result.NuevoWatermark, ct);
```

> Solo se persiste si `Success` y `NuevoWatermark != null` (si no vino ninguna fila, `NuevoWatermark` es null y NO se toca el watermark guardado — la proxima corrida reintenta desde el mismo punto).

- [ ] **Step 4: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Engine/EjecutorProgramado.cs tests/DataPipeline.Tests/EjecutorProgramadoTests.cs
git commit -m "feat(engine): EjecutorProgramado persiste el watermark tras corrida exitosa"
```

---

### Task 6: `RunsController` persiste el watermark en el run on-demand (build-only)

**Files:**
- Modify: `DataPipeline.Api/Controllers/RunsController.cs`

**Interfaces:**
- Consumes: `IPipelineStore.ActualizarWatermarkAsync` (Task 4); `RunResult.Success`, `RunResult.NuevoWatermark` (Task 2).

> **Por que build-only:** el proyecto de tests de API prueba endpoints via HTTP; el efecto es identico al de `EjecutorProgramado` (ya cubierto por Task 5). Se agrega la misma llamada de forma consistente. Si el archivo de tests de API tiene un patron simple para verificar el efecto sobre el store, agregar un test es bienvenido, pero no es obligatorio.

- [ ] **Step 1: Persistir el watermark tras la corrida on-demand**

En `RunsController.Run`, despues de `CompletarAsync` (linea 39) y antes del `return Ok(...)`:

```csharp
        if (result.Success && result.NuevoWatermark is not null)
            await _store.ActualizarWatermarkAsync(def.Id, result.NuevoWatermark, ct);
```

- [ ] **Step 2: Verificar build sin warnings**

Run: `dotnet build DataPipeline.Api --no-incremental --nologo`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add DataPipeline.Api/Controllers/RunsController.cs
git commit -m "feat(api): RunsController persiste el watermark tras corrida on-demand"
```

---

### Task 7: Editor — input "Columna watermark" (build-only)

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor`

**Interfaces:**
- Consumes: `PipelineDefinition.ColumnaWatermark` (Task 1).

> **Por que build-only:** la UI Blazor/Syncfusion se prueba manual (decision del usuario, igual que 3C-4A).

- [ ] **Step 1: Agregar el campo al `PipelineForm`**

En `Editor.razor`, en `PipelineForm` (despues de `ColumnasClave`, linea 164):

```csharp
        public string? ColumnaWatermark { get; set; } = ""; // columna del origen a trackear; vacio = no incremental
```

- [ ] **Step 2: Agregar el input en la card "Generales"**

En la card "Generales", despues del `<div class="mb-3">` de "Columnas clave" (linea 37), agregar:

```razor
            <div class="mb-3">
                <SfTextBox Placeholder="Columna watermark (opcional — carga incremental)"
                           FloatLabelType="FloatLabelType.Always" @bind-Value="_form.ColumnaWatermark" />
                <div class="form-text">
                    Si la usas, tu SQL debe filtrar con @@watermark (ej. WHERE @@watermark IS NULL OR fecha_mod &gt; @@watermark).
                    Vaciar este campo y guardar resetea el watermark (la proxima corrida trae todo).
                </div>
            </div>
```

> Nota Razor: `@@` produce un `@` literal en el markup. Usar `@@watermark` para que se muestre `@watermark`.

- [ ] **Step 3: Mapear en `ConstruirDef` (guardar) — incluye el reset del watermark**

En `ConstruirDef`, en el `new PipelineDefinition { ... }` (despues de `ColumnasClave = ...`, linea 306-308), agregar:

```csharp
            ColumnaWatermark = string.IsNullOrWhiteSpace(_form.ColumnaWatermark)
                ? null
                : _form.ColumnaWatermark!.Trim(),
```

> **Reset:** al no setear `UltimoWatermark` en `ConstruirDef`, se guarda como null (default). Es decir, guardar desde el editor resetea el watermark a "trae todo". Esto implementa el reset del spec (decision 4) de la forma mas simple: editar + guardar re-arranca el incremental. (El `UltimoWatermark` no se edita a mano.)

- [ ] **Step 4: Mapear en `Cargar` (editar existente)**

En `Cargar`, despues de `_form.ColumnasClave = ...` (linea 332):

```csharp
        _form.ColumnaWatermark = def.ColumnaWatermark ?? "";
```

- [ ] **Step 5: Verificar build sin warnings**

Run: `dotnet build DataPipeline.Admin --no-incremental --nologo`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "feat(admin): editor captura ColumnaWatermark + reset al guardar [build-only]"
```

---

### Task 8: Verificacion final — build limpio + suite completa

**Files:** (ninguno — solo verificacion)

- [ ] **Step 1: Build completo sin warnings (gate de 0 warnings)**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Suite completa verde**

Run: `dotnet test --nologo`
Expected: todos los tests pasan (los 58 previos + los nuevos de Tasks 1, 2, 4, 5).

- [ ] **Step 3: Confirmar historia de commits limpia**

Run: `git log --oneline -9`
Expected: los commits de Tasks 1-7 en `main`, sin haber usado `git add -A`.

---

## Notas de contrato (recordatorio para quien ejecute)

- **Modo Agregar + watermark:** casi obligatorio. Sin watermark, releer todo + Agregar duplica en el serving.
- **Modo Reemplazar + watermark:** optimizacion (el upsert por `_key` no duplica), pero el serving queda como "replica" que acumula keys viejas (upsert puro no borra ausentes). Limpiar lo viejo es decision del usuario (fuera de alcance de 4C).
- **El operador (`>` vs `>=`) y el casteo del watermark** los decide el usuario en su SQL. 4C no reescribe el SQL.
- Fuera de alcance: watermark compuesto (varias columnas), tipado fuerte, ventanas/solapamiento.
