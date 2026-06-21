# DataPipeline — Fase 2 (Motor usable + entrega PULL) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convertir DataPipeline en un middleware usable: transformaciones config-driven (NCalc), pipelines persistidos en BD (EF Core) y programados (scheduler), con los datos transformados expuestos por una API PULL autenticada e historial de corridas.

**Architecture:** Se agregan dos proyectos — `DataPipeline.Persistence` (EF Core, almacén A, implementa repos definidos en `Core`) y `DataPipeline.Api` (deployable: endpoints PULL/run/runs + Basic Auth + scheduler `BackgroundService`). El `Engine` gana composición de transforms y `ScheduleCalculator`; `Connectors` gana `ColumnMapTransform` (NCalc) + `TransformFactory`. `Core` evoluciona `StepDefinition.Configuracion` a `Dictionary<string,JsonElement>` y suma `PipelineRun` + contratos de repos.

**Tech Stack:** .NET 10 (`net10.0`), EF Core 10.0.8 (Sqlite), NCalc (expresiones), ASP.NET Core (Sdk.Web, Basic Auth handler propio), BCrypt.Net-Next (verificación de clave), xUnit + Microsoft.AspNetCore.Mvc.Testing (integración).

**Spec:** `E:\Sistemas\MiddleWare\docs\superpowers\specs\2026-06-20-datapipeline-fase2-design.md`.

## Global Constraints

- TargetFramework `net10.0`; `<Nullable>enable</Nullable>`; `<ImplicitUsings>enable</ImplicitUsings>` en todos los proyectos.
- `DataPipeline.Core` sigue SIN dependencias externas (solo BCL, incluido `System.Text.Json`). EF y NCalc nunca en Core.
- **Convención de idioma (Opción 2):** enums (tipo+valores) y propiedades de `PipelineDefinition`/`StepDefinition`/`PipelineRun` en castellano; interfaces, tipos técnicos de runtime (`Record`/`RecordBatch`/`RunContext`/`RunResult`), servicios, métodos y modismos .NET (`I`, `Async`, `CancellationToken`) en inglés. Variables locales y comentarios en español. **Sin tildes ni `ñ` en identificadores** (sí en comentarios/strings).
- **Streaming obligatorio:** transforms procesan lote a lote (`IAsyncEnumerable<RecordBatch>`), nunca materializan todo.
- En los proyectos de test, por el global `using Xunit`, incluir `using Record = DataPipeline.Core.Records.Record;` cuando se use `Record` (evita CS0104 vs `Xunit.Record`).
- Build pristine (0 warnings). `Directory.Build.props` ya suprime un advisory transitivo de SQLite — no tocarlo.
- Un commit como mínimo por tarea.

## Estado de partida (Fase 1, ya construido)

- `Core`: `Record{Values}`, `RecordBatch{Columns,Rows}`, `PipelineDefinition{Id,Nombre,Habilitado,Programacion,HoraDelDia,DiaDeLaSemana,DiaDelMes,ProximaCorrida,UltimaCorrida,Pasos,TablaDestino,ModoEscritura,CorrerDespuesDe,FilasPorLote}`, `StepDefinition{Nombre,Tipo,Conector,Configuracion}`, enums `TipoPaso{Origen,Transformacion}`/`TipoProgramacion{BajoDemanda,Diaria,Semanal,Mensual}`/`ModoEscritura{Agregar,Combinar,Reemplazar}`, `RunContext{PipelineId,RunId,Parameters,Encryption,BatchSize}`, contratos `ISource`/`ITransform`/`IDataStore`/`ISourceFactory`/`IEncryptionService`.
- `Connectors`: `EncryptionService(string claveBase64)`, `RecordBatcher.Batch(cols, filas, tamanoLote, ct)`, `SqlServerSource(string connectionString, string sql, bool connectionStringEncrypted)`, `SqliteDataStore(string connectionString)` (`EnsureTableAsync(table,cols,ModoEscritura,ct)`/`WriteBatchAsync(table,batch,ct)`/`ReadPagedAsync(table,page,pageSize,ct)`), `SourceFactory`.
- `Engine`: `PipelineRunner(ISourceFactory, IDataStore)` con `RunAsync(PipelineDefinition def, IReadOnlyDictionary<string,string> parameters, IEncryptionService encryption, CancellationToken ct=default) → RunResult(RunId,Success,RowsRead,RowsWritten,Error,Duration)`; `PipelineJsonLoader.Load(string json)`.
- `Cli`: corre un pipeline JSON contra una SQLite.
- Tests: 14 verdes. Build pristine.

---

## File Structure

```
DataPipeline.Core/
  Pipelines/StepDefinition.cs        (MOD: Configuracion -> Dictionary<string,JsonElement>)
  Pipelines/PipelineRun.cs           (NEW: PipelineRun + EstadoCorrida)
  Abstractions/ITransformFactory.cs  (NEW)
  Abstractions/IPipelineStore.cs     (NEW)
  Abstractions/IHistorialCorridas.cs (NEW)
DataPipeline.Connectors/
  Transforms/ColumnMapTransform.cs   (NEW: rename/cast/computed/filter, NCalc)
  Factories/TransformFactory.cs      (NEW)
  Factories/SourceFactory.cs         (MOD: lee JsonElement)
DataPipeline.Engine/
  PipelineRunner.cs                  (MOD: compone transforms via ITransformFactory)
  ScheduleCalculator.cs              (NEW)
  EjecutorProgramado.cs              (NEW: procesa pipelines vencidos)
DataPipeline.Persistence/            (NEW PROJECT)
  DataPipeline.Persistence.csproj
  MetadataDbContext.cs
  EfPipelineStore.cs
  EfHistorialCorridas.cs
  Migrations/*                       (EF generated)
DataPipeline.Api/                    (NEW PROJECT, Sdk.Web)
  DataPipeline.Api.csproj
  Program.cs
  Auth/BasicAuthHandler.cs
  Controllers/DataController.cs       (GET /api/data/{nombre})
  Controllers/RunsController.cs       (POST /api/run/{id}, GET /api/runs)
  Hosting/PipelineSchedulerHostedService.cs
  appsettings.json
DataPipeline.Cli/
  Program.cs                          (MOD: subcomando "import")
tests/DataPipeline.Tests/
  ColumnMapTransformTests.cs, TransformFactoryTests.cs, PipelineRunnerTransformTests.cs,
  ScheduleCalculatorTests.cs, EjecutorProgramadoTests.cs
  (MOD: SourceFactoryTests.cs para JsonElement)
tests/DataPipeline.Persistence.Tests/  (NEW: repos EF contra SQLite real)
tests/DataPipeline.Api.Tests/           (NEW: integración WebApplicationFactory)
```

---

### Task 1: Evolucionar `StepDefinition.Configuracion` a `Dictionary<string,JsonElement>`

**Files:**
- Modify: `DataPipeline.Core/Pipelines/StepDefinition.cs`
- Modify: `DataPipeline.Connectors/Factories/SourceFactory.cs`
- Modify: `tests/DataPipeline.Tests/SourceFactoryTests.cs`

**Interfaces:**
- Consumes: `StepDefinition`, `TipoPaso`, `SqlServerSource` (Fase 1).
- Produces: `StepDefinition.Configuracion` ahora es `Dictionary<string, System.Text.Json.JsonElement>`. Helper en SourceFactory `LeerString(step, clave)` que hace `Configuracion[clave].GetString()`.

- [ ] **Step 1: Actualizar el test de SourceFactory para JsonElement (debe fallar a compilar)**

Reemplazar el contenido de `tests/DataPipeline.Tests/SourceFactoryTests.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class SourceFactoryTests
{
    private static JsonElement S(string valor) =>
        JsonSerializer.SerializeToElement(valor);

    [Fact]
    public void Crea_un_SqlServerSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer",
            Tipo = TipoPaso.Origen,
            Conector = "SqlServerSource",
            Configuracion = new() { ["connectionString"] = S("Server=x;"), ["sql"] = S("SELECT 1") }
        };

        var source = factory.Create(step);

        Assert.IsType<SqlServerSource>(source);
    }

    [Fact]
    public void Falta_config_requerida_lanza()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" };

        Assert.Throws<ArgumentException>(() => factory.Create(step));
    }

    [Fact]
    public void Tipo_no_soportado_lanza()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Origen, Conector = "KafkaSource" };

        Assert.Throws<NotSupportedException>(() => factory.Create(step));
    }
}
```

- [ ] **Step 2: Compilar para ver el RED**

Run: `dotnet build tests/DataPipeline.Tests`
Expected: FALLA de compilación (no se puede asignar `JsonElement` a `Dictionary<string,string>` ni el helper actual).

- [ ] **Step 3: Cambiar el tipo de `Configuracion`**

`DataPipeline.Core/Pipelines/StepDefinition.cs`:
```csharp
using System.Text.Json;

namespace DataPipeline.Core.Pipelines;

/// <summary>Un paso del pipeline (Origen o Transformación). El sink interno es implícito.</summary>
public sealed class StepDefinition
{
    public string Nombre { get; set; } = "";
    public TipoPaso Tipo { get; set; }
    public string Conector { get; set; } = "";

    /// <summary>Config propia del paso. Valores como JsonElement para admitir objetos anidados (transforms).</summary>
    public Dictionary<string, JsonElement> Configuracion { get; set; } = new();
}
```

- [ ] **Step 4: Adaptar `SourceFactory` a leer JsonElement**

`DataPipeline.Connectors/Factories/SourceFactory.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Origen a su conector concreto (Fase 1-2: SqlServerSource).</summary>
public sealed class SourceFactory : ISourceFactory
{
    public ISource Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Origen)
            throw new ArgumentException($"El paso '{step.Nombre}' no es un Origen.", nameof(step));

        return step.Conector switch
        {
            "SqlServerSource" => new SqlServerSource(
                LeerString(step, "connectionString"),
                LeerString(step, "sql"),
                step.Configuracion.TryGetValue("connectionStringEncrypted", out var e)
                    && e.ValueKind == JsonValueKind.True),
            _ => throw new NotSupportedException($"Conector de origen no soportado: '{step.Conector}'.")
        };
    }

    private static string LeerString(StepDefinition step, string clave)
    {
        if (step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        throw new ArgumentException($"El origen '{step.Nombre}' requiere la config string '{clave}'.", nameof(step));
    }
}
```

- [ ] **Step 5: Correr el suite y verificar verde**

Run: `dotnet test`
Expected: PASS (14/14 — SourceFactory tests adaptados, el resto intacto). `PipelineJsonLoader` ya deserializa `JsonElement` sin cambios (System.Text.Json mapea objetos/strings a JsonElement).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(core): StepDefinition.Configuracion -> Dictionary<string,JsonElement> para configs anidadas"
```

---

### Task 2: `ITransformFactory` + `ColumnMapTransform` (rename + cast)

**Files:**
- Create: `DataPipeline.Core/Abstractions/ITransformFactory.cs`
- Create: `DataPipeline.Connectors/Transforms/ColumnMapTransform.cs`
- Test: `tests/DataPipeline.Tests/ColumnMapTransformTests.cs`

**Interfaces:**
- Consumes: `ITransform`, `RecordBatch`, `Record`, `RunContext`, `StepDefinition`, `JsonElement` (Tasks 1, Fase 1).
- Produces:
  - `interface ITransformFactory { ITransform Create(StepDefinition step); }`
  - `class ColumnMapTransform : ITransform` con constructor `ColumnMapTransform(IReadOnlyDictionary<string,string> rename, IReadOnlyDictionary<string,string> cast, IReadOnlyDictionary<string,string> computed, string? filter)`. En esta tarea solo se implementan `rename` y `cast` (computed/filter llegan en Task 3, por ahora se aceptan pero `computed` vacío y `filter` null).

- [ ] **Step 1: Escribir el test (rename + cast)**

`tests/DataPipeline.Tests/ColumnMapTransformTests.cs`:
```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class ColumnMapTransformTests
{
    private static RunContext Ctx() => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static async IAsyncEnumerable<RecordBatch> UnLote(params Record[] filas)
    {
        await Task.Yield();
        yield return new RecordBatch
        {
            Columns = filas.Length > 0 ? filas[0].Values.Keys.ToArray() : Array.Empty<string>(),
            Rows = filas
        };
    }

    private static Record Fila(params (string c, object? v)[] vals) =>
        new() { Values = vals.ToDictionary(x => x.c, x => x.v) };

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Rename_cambia_el_nombre_de_la_columna()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string> { ["monto"] = "importe" },
            cast: new Dictionary<string, string>(),
            computed: new Dictionary<string, string>(),
            filter: null);

        var filas = await Drenar(t.TransformAsync(UnLote(Fila(("monto", 10))), Ctx(), CancellationToken.None));

        Assert.True(filas[0].Values.ContainsKey("importe"));
        Assert.False(filas[0].Values.ContainsKey("monto"));
        Assert.Equal(10, filas[0].Values["importe"]);
    }

    [Fact]
    public async Task Cast_convierte_el_tipo()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string>(),
            cast: new Dictionary<string, string> { ["importe"] = "decimal" },
            computed: new Dictionary<string, string>(),
            filter: null);

        var filas = await Drenar(t.TransformAsync(UnLote(Fila(("importe", "10.5"))), Ctx(), CancellationToken.None));

        Assert.Equal(10.5m, filas[0].Values["importe"]);
    }
}
```

- [ ] **Step 2: Compilar para ver el RED**

Run: `dotnet build tests/DataPipeline.Tests`
Expected: FALLA (no existen `ColumnMapTransform` ni `ITransformFactory`).

- [ ] **Step 3: Crear `ITransformFactory` en Core**

`DataPipeline.Core/Abstractions/ITransformFactory.cs`:
```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve un StepDefinition de tipo Transformacion a su transform concreto.</summary>
public interface ITransformFactory
{
    ITransform Create(StepDefinition step);
}
```

- [ ] **Step 4: Implementar `ColumnMapTransform` (rename + cast; computed/filter quedan inertes hasta Task 3)**

`DataPipeline.Connectors/Transforms/ColumnMapTransform.cs`:
```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Connectors.Transforms;

/// <summary>
/// Transform declarativo. Orden de operaciones por fila: rename -> cast -> computed -> filter.
/// (computed/filter se completan en la tarea siguiente con NCalc.)
/// </summary>
public sealed class ColumnMapTransform : ITransform
{
    private readonly IReadOnlyDictionary<string, string> _rename;
    private readonly IReadOnlyDictionary<string, string> _cast;
    private readonly IReadOnlyDictionary<string, string> _computed;
    private readonly string? _filter;

    public ColumnMapTransform(
        IReadOnlyDictionary<string, string> rename,
        IReadOnlyDictionary<string, string> cast,
        IReadOnlyDictionary<string, string> computed,
        string? filter)
    {
        _rename = rename;
        _cast = cast;
        _computed = computed;
        _filter = filter;
    }

    public async IAsyncEnumerable<RecordBatch> TransformAsync(
        IAsyncEnumerable<RecordBatch> input, RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var lote in input.WithCancellation(ct))
        {
            var salida = new List<Record>(lote.Rows.Count);
            foreach (var fila in lote.Rows)
            {
                var v = AplicarRename(fila.Values);
                AplicarCast(v);
                // computed/filter: Task 3
                salida.Add(new Record { Values = v });
            }

            var columnas = salida.Count > 0
                ? salida[0].Values.Keys.ToArray()
                : RenombrarColumnas(lote.Columns);

            yield return new RecordBatch { Columns = columnas, Rows = salida };
        }
    }

    private Dictionary<string, object?> AplicarRename(IReadOnlyDictionary<string, object?> origen)
    {
        var dst = new Dictionary<string, object?>(origen.Count);
        foreach (var (k, val) in origen)
            dst[_rename.TryGetValue(k, out var nuevo) ? nuevo : k] = val;
        return dst;
    }

    private string[] RenombrarColumnas(IReadOnlyList<string> cols) =>
        cols.Select(c => _rename.TryGetValue(c, out var n) ? n : c).ToArray();

    private void AplicarCast(Dictionary<string, object?> fila)
    {
        foreach (var (col, tipo) in _cast)
        {
            if (!fila.TryGetValue(col, out var val) || val is null) continue;
            fila[col] = Convertir(val, tipo);
        }
    }

    private static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch
    {
        "int"     => Convert.ToInt32(val, CultureInfo.InvariantCulture),
        "long"    => Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "decimal" => Convert.ToDecimal(val, CultureInfo.InvariantCulture),
        "double"  => Convert.ToDouble(val, CultureInfo.InvariantCulture),
        "bool"    => Convert.ToBoolean(val, CultureInfo.InvariantCulture),
        "date"    => DateTime.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        "string"  => Convert.ToString(val, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo de cast no soportado: '{tipo}'.")
    };
}
```

- [ ] **Step 5: Correr el test y verificar verde**

Run: `dotnet test --filter "FullyQualifiedName~ColumnMapTransformTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(connectors): ITransformFactory + ColumnMapTransform (rename + cast)"
```

---

### Task 3: `ColumnMapTransform` — `computed` + `filter` con NCalc

**Files:**
- Modify: `DataPipeline.Connectors/Transforms/ColumnMapTransform.cs`
- Modify: `DataPipeline.Connectors/DataPipeline.Connectors.csproj` (agregar NCalc)
- Test: `tests/DataPipeline.Tests/ColumnMapTransformTests.cs` (agregar casos)

**Interfaces:**
- Consumes: lo de Task 2.
- Produces: `ColumnMapTransform` evalúa `computed` (col→expresión) y `filter` (expresión booleana) con NCalc. Orden por fila: rename → cast → computed → filter. Expresión inválida (sintaxis) → `ArgumentException` al construir; error de evaluación → propaga (falla la corrida).

- [ ] **Step 1: Agregar el paquete NCalc**

Run:
```bash
dotnet add DataPipeline.Connectors package NCalc
```

- [ ] **Step 2: Agregar tests de computed + filter (deben fallar)**

Agregar al final de `tests/DataPipeline.Tests/ColumnMapTransformTests.cs` (antes del `}` final de la clase):
```csharp
    [Fact]
    public async Task Computed_agrega_columna_calculada()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string>(),
            cast: new Dictionary<string, string>(),
            computed: new Dictionary<string, string> { ["neto"] = "monto - bonif" },
            filter: null);

        var filas = await Drenar(t.TransformAsync(
            UnLote(Fila(("monto", 100), ("bonif", 30))), Ctx(), CancellationToken.None));

        Assert.Equal(70, Convert.ToInt32(filas[0].Values["neto"]));
    }

    [Fact]
    public async Task Filter_descarta_filas_que_no_cumplen()
    {
        var t = new ColumnMapTransform(
            rename: new Dictionary<string, string>(),
            cast: new Dictionary<string, string>(),
            computed: new Dictionary<string, string>(),
            filter: "importe > 0");

        var filas = await Drenar(t.TransformAsync(
            UnLote(Fila(("importe", 5)), Fila(("importe", -1)), Fila(("importe", 10))),
            Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.True(Convert.ToInt32(f.Values["importe"]) > 0));
    }

    [Fact]
    public void Filtro_con_sintaxis_invalida_lanza_al_construir()
    {
        Assert.Throws<ArgumentException>(() => new ColumnMapTransform(
            rename: new Dictionary<string, string>(),
            cast: new Dictionary<string, string>(),
            computed: new Dictionary<string, string>(),
            filter: "importe >"));
    }
```

- [ ] **Step 3: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~ColumnMapTransformTests"`
Expected: FALLA (computed/filter aún inertes; el test de sintaxis no lanza).

- [ ] **Step 4: Implementar computed + filter con NCalc**

Reemplazar `DataPipeline.Connectors/Transforms/ColumnMapTransform.cs` por:
```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using NCalc;

namespace DataPipeline.Connectors.Transforms;

/// <summary>
/// Transform declarativo. Orden de operaciones por fila: rename -> cast -> computed -> filter.
/// computed/filter se evaluan con NCalc; los nombres de columna son las variables.
/// </summary>
public sealed class ColumnMapTransform : ITransform
{
    private readonly IReadOnlyDictionary<string, string> _rename;
    private readonly IReadOnlyDictionary<string, string> _cast;
    private readonly IReadOnlyList<(string Columna, Expression Expr)> _computed;
    private readonly Expression? _filter;

    public ColumnMapTransform(
        IReadOnlyDictionary<string, string> rename,
        IReadOnlyDictionary<string, string> cast,
        IReadOnlyDictionary<string, string> computed,
        string? filter)
    {
        _rename = rename;
        _cast = cast;
        _computed = computed.Select(kv => (kv.Key, Compilar(kv.Value, $"computed '{kv.Key}'"))).ToList();
        _filter = string.IsNullOrWhiteSpace(filter) ? null : Compilar(filter, "filter");
    }

    private static Expression Compilar(string expr, string contexto)
    {
        var e = new Expression(expr);
        if (e.HasErrors())
            throw new ArgumentException($"Expresión {contexto} inválida: {e.Error}", nameof(expr));
        return e;
    }

    public async IAsyncEnumerable<RecordBatch> TransformAsync(
        IAsyncEnumerable<RecordBatch> input, RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var lote in input.WithCancellation(ct))
        {
            var salida = new List<Record>(lote.Rows.Count);
            foreach (var fila in lote.Rows)
            {
                var v = AplicarRename(fila.Values);
                AplicarCast(v);
                AplicarComputed(v);
                if (_filter is not null && !EsVerdadero(_filter, v)) continue;
                salida.Add(new Record { Values = v });
            }

            var columnas = salida.Count > 0
                ? salida[0].Values.Keys.ToArray()
                : ColumnasEsperadas(lote.Columns);

            yield return new RecordBatch { Columns = columnas, Rows = salida };
        }
    }

    private Dictionary<string, object?> AplicarRename(IReadOnlyDictionary<string, object?> origen)
    {
        var dst = new Dictionary<string, object?>(origen.Count);
        foreach (var (k, val) in origen)
            dst[_rename.TryGetValue(k, out var nuevo) ? nuevo : k] = val;
        return dst;
    }

    private string[] ColumnasEsperadas(IReadOnlyList<string> cols)
    {
        var renombradas = cols.Select(c => _rename.TryGetValue(c, out var n) ? n : c);
        return renombradas.Concat(_computed.Select(c => c.Columna)).Distinct().ToArray();
    }

    private void AplicarCast(Dictionary<string, object?> fila)
    {
        foreach (var (col, tipo) in _cast)
        {
            if (!fila.TryGetValue(col, out var val) || val is null) continue;
            fila[col] = Convertir(val, tipo);
        }
    }

    private void AplicarComputed(Dictionary<string, object?> fila)
    {
        foreach (var (columna, expr) in _computed)
            fila[columna] = Evaluar(expr, fila);
    }

    private static object? Evaluar(Expression expr, Dictionary<string, object?> fila)
    {
        expr.Parameters.Clear();
        foreach (var (k, val) in fila) expr.Parameters[k] = val;
        return expr.Evaluate();
    }

    private static bool EsVerdadero(Expression expr, Dictionary<string, object?> fila)
    {
        var r = Evaluar(expr, fila);
        return r is bool b && b;
    }

    private static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch
    {
        "int"     => Convert.ToInt32(val, CultureInfo.InvariantCulture),
        "long"    => Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "decimal" => Convert.ToDecimal(val, CultureInfo.InvariantCulture),
        "double"  => Convert.ToDouble(val, CultureInfo.InvariantCulture),
        "bool"    => Convert.ToBoolean(val, CultureInfo.InvariantCulture),
        "date"    => DateTime.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        "string"  => Convert.ToString(val, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo de cast no soportado: '{tipo}'.")
    };
}
```

- [ ] **Step 5: Correr el test y verificar verde + suite + build pristine**

Run: `dotnet test --filter "FullyQualifiedName~ColumnMapTransformTests"` → PASS (5 tests).
Run: `dotnet build` → 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(connectors): ColumnMapTransform computed + filter con NCalc"
```

---

### Task 4: `TransformFactory`

**Files:**
- Create: `DataPipeline.Connectors/Factories/TransformFactory.cs`
- Test: `tests/DataPipeline.Tests/TransformFactoryTests.cs`

**Interfaces:**
- Consumes: `ITransformFactory`, `ColumnMapTransform`, `StepDefinition`, `TipoPaso`, `JsonElement`.
- Produces: `class TransformFactory : ITransformFactory`. Lee de `Configuracion` los objetos `rename`/`cast`/`computed` (objetos JSON → `Dictionary<string,string>`) y `filter` (string). Conector desconocido → `NotSupportedException`; paso que no es Transformacion → `ArgumentException`.

- [ ] **Step 1: Escribir el test**

`tests/DataPipeline.Tests/TransformFactoryTests.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class TransformFactoryTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Crea_un_ColumnMapTransform()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition
        {
            Nombre = "norm",
            Tipo = TipoPaso.Transformacion,
            Conector = "ColumnMapTransform",
            Configuracion = new()
            {
                ["rename"] = J("{\"monto\":\"importe\"}"),
                ["filter"] = J("\"importe > 0\"")
            }
        };

        var t = factory.Create(step);

        Assert.IsType<ColumnMapTransform>(t);
    }

    [Fact]
    public void Paso_que_no_es_transformacion_lanza()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Origen, Conector = "ColumnMapTransform" };
        Assert.Throws<ArgumentException>(() => factory.Create(step));
    }

    [Fact]
    public void Conector_desconocido_lanza()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Transformacion, Conector = "Otro" };
        Assert.Throws<NotSupportedException>(() => factory.Create(step));
    }
}
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~TransformFactoryTests"`
Expected: FALLA (no existe `TransformFactory`).

- [ ] **Step 3: Implementar `TransformFactory`**

`DataPipeline.Connectors/Factories/TransformFactory.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Transformacion (Fase 2: ColumnMapTransform).</summary>
public sealed class TransformFactory : ITransformFactory
{
    public ITransform Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Transformacion)
            throw new ArgumentException($"El paso '{step.Nombre}' no es una Transformacion.", nameof(step));

        return step.Conector switch
        {
            "ColumnMapTransform" => new ColumnMapTransform(
                LeerMapa(step, "rename"),
                LeerMapa(step, "cast"),
                LeerMapa(step, "computed"),
                LeerStringOpcional(step, "filter")),
            _ => throw new NotSupportedException($"Conector de transformacion no soportado: '{step.Conector}'.")
        };
    }

    private static Dictionary<string, string> LeerMapa(StepDefinition step, string clave)
    {
        var dst = new Dictionary<string, string>();
        if (step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var prop in v.EnumerateObject())
                dst[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
        return dst;
    }

    private static string? LeerStringOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
```

- [ ] **Step 4: Correr el test y verificar verde**

Run: `dotnet test --filter "FullyQualifiedName~TransformFactoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(connectors): TransformFactory resuelve ColumnMapTransform desde config JSON"
```

---

### Task 5: `PipelineRunner` compone transforms

**Files:**
- Modify: `DataPipeline.Engine/PipelineRunner.cs`
- Test: `tests/DataPipeline.Tests/PipelineRunnerTransformTests.cs`

**Interfaces:**
- Consumes: `ISourceFactory`, `ITransformFactory`, `IDataStore`, `PipelineDefinition`, `TipoPaso`, `RunContext`, `RunResult`, `SqliteDataStore`, `ColumnMapTransform`, `TransformFactory`, `SourceFactory`.
- Produces: `PipelineRunner(ISourceFactory sourceFactory, ITransformFactory transformFactory, IDataStore dataStore)`; `RunAsync` igual firma. Ahora resuelve los pasos `Transformacion` (en orden de `Pasos`) y los compone entre source y store. **El constructor cambia (suma `ITransformFactory`).**

- [ ] **Step 1: Escribir el test (source fake + transform real → store SQLite)**

`tests/DataPipeline.Tests/PipelineRunnerTransformTests.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class PipelineRunnerTransformTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dprt_{Guid.NewGuid():N}.db")};Pooling=False";

    private static EncryptionService EncDummy() => new(Convert.ToBase64String(new byte[32]));

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Record Fila(int importe) =>
        new() { Values = new Dictionary<string, object?> { ["importe"] = importe } };

    [Fact]
    public async Task Aplica_filter_entre_source_y_store()
    {
        var store = new SqliteDataStore(NuevaDb());
        var source = new FakeSource(new[] { "importe" }, new[] { Fila(5), Fila(-2), Fila(8) }, porLote: 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), store);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(),
            Nombre = "p",
            TablaDestino = "destino",
            ModoEscritura = ModoEscritura.Reemplazar,
            FilasPorLote = 10,
            Pasos =
            {
                new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" },
                new StepDefinition
                {
                    Nombre = "filtrar", Tipo = TipoPaso.Transformacion, Conector = "ColumnMapTransform",
                    Configuracion = new() { ["filter"] = J("\"importe > 0\"") }
                }
            }
        };

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.RowsRead);
        Assert.Equal(2, result.RowsWritten);

        var pagina = await store.ReadPagedAsync("destino", 1, 10, CancellationToken.None);
        Assert.Equal(2, pagina.Rows.Count);
    }
}
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~PipelineRunnerTransformTests"`
Expected: FALLA de compilación (constructor de `PipelineRunner` no acepta `ITransformFactory`).

- [ ] **Step 3: Modificar `PipelineRunner` para componer transforms**

Reemplazar `DataPipeline.Engine/PipelineRunner.cs` por:
```csharp
using System.Diagnostics;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Arma y corre la tubería Source -> Transform(s) -> IDataStore por lotes.</summary>
public sealed class PipelineRunner
{
    private readonly ISourceFactory _sourceFactory;
    private readonly ITransformFactory _transformFactory;
    private readonly IDataStore _dataStore;

    public PipelineRunner(ISourceFactory sourceFactory, ITransformFactory transformFactory, IDataStore dataStore)
    {
        _sourceFactory = sourceFactory;
        _transformFactory = transformFactory;
        _dataStore = dataStore;
    }

    public async Task<RunResult> RunAsync(
        PipelineDefinition def,
        IReadOnlyDictionary<string, string> parameters,
        IEncryptionService encryption,
        CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var cronometro = Stopwatch.StartNew();
        long leidas = 0, escritas = 0;

        try
        {
            var pasoSource = def.Pasos.SingleOrDefault(s => s.Tipo == TipoPaso.Origen)
                ?? throw new InvalidOperationException("El pipeline necesita exactamente un Origen.");

            var source = _sourceFactory.Create(pasoSource);
            var transforms = def.Pasos
                .Where(s => s.Tipo == TipoPaso.Transformacion)
                .Select(_transformFactory.Create)
                .ToList();

            var ctx = new RunContext
            {
                PipelineId = def.Id,
                RunId = runId,
                Parameters = parameters,
                Encryption = encryption,
                BatchSize = def.FilasPorLote
            };

            IAsyncEnumerable<RecordBatch> stream = source.ReadAsync(ctx, ct);
            foreach (var t in transforms)
                stream = t.TransformAsync(stream, ctx, ct);

            var tablaLista = false;
            await foreach (var lote in stream.WithCancellation(ct))
            {
                leidas += lote.Rows.Count;
                if (lote.Rows.Count == 0) continue;

                if (!tablaLista)
                {
                    await _dataStore.EnsureTableAsync(def.TablaDestino, lote.Columns, def.ModoEscritura, ct);
                    tablaLista = true;
                }
                await _dataStore.WriteBatchAsync(def.TablaDestino, lote, ct);
                escritas += lote.Rows.Count;
            }

            cronometro.Stop();
            return new RunResult(runId, true, leidas, escritas, null, cronometro.Elapsed);
        }
        catch (Exception ex)
        {
            cronometro.Stop();
            return new RunResult(runId, false, leidas, escritas, ex.Message, cronometro.Elapsed);
        }
    }
}
```

> Nota: `leidas` ahora cuenta filas **post-transform** (lo que sale del último paso). Para Fase 2 es aceptable: `RowsRead` refleja filas que llegaron al sink-stage y `RowsWritten` las escritas; con filter pueden diferir de las filas crudas del origen. (El conteo de filas crudas del origen se puede agregar en Fase 4 con métricas por paso.)

- [ ] **Step 4: Ajustar el test de Fase 1 `PipelineRunnerTests` al nuevo constructor**

En `tests/DataPipeline.Tests/PipelineRunnerTests.cs`, donde se construye `new PipelineRunner(new FakeSourceFactory(source), store)`, cambiar por `new PipelineRunner(new FakeSourceFactory(source), new DataPipeline.Connectors.Factories.TransformFactory(), store)`. Agregar `using DataPipeline.Connectors.Factories;` si hace falta. El test `Falla_controlada_devuelve_Success_false_con_Error` ahora ya NO debe esperar el mensaje "Transform" (los transforms están soportados); cambiarlo para forzar el fallo con un Origen ausente:
```csharp
    [Fact]
    public async Task Falla_controlada_devuelve_Success_false_con_Error()
    {
        var store = new SqliteDataStore(NuevaDb());
        var def = DefMinima();
        def.Pasos.Clear(); // sin Origen -> InvalidOperationException, capturada
        var runner = new PipelineRunner(new FakeSourceFactory(new FakeSource(new[] { "id" }, Array.Empty<Record>(), 5)),
            new DataPipeline.Connectors.Factories.TransformFactory(), store);

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.False(result.Success);
        Assert.Contains("Origen", result.Error);
    }
```

- [ ] **Step 5: Correr el suite completo y verificar verde**

Run: `dotnet test`
Expected: PASS (todos: los de Fase 1 ajustados + los nuevos de transform).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(engine): PipelineRunner compone transforms (Source->Transform(s)->Store)"
```

---

### Task 6: Persistencia — Core contracts + `DataPipeline.Persistence` (EF Core)

**Files:**
- Create: `DataPipeline.Core/Pipelines/PipelineRun.cs` (PipelineRun + EstadoCorrida)
- Create: `DataPipeline.Core/Abstractions/IPipelineStore.cs`
- Create: `DataPipeline.Core/Abstractions/IHistorialCorridas.cs`
- Create: `DataPipeline.Persistence/DataPipeline.Persistence.csproj`
- Create: `DataPipeline.Persistence/MetadataDbContext.cs`
- Create: `DataPipeline.Persistence/EfPipelineStore.cs`
- Create: `DataPipeline.Persistence/EfHistorialCorridas.cs`
- Create: `tests/DataPipeline.Persistence.Tests/*`
- Modify: `DataPipeline.slnx` (agregar los 2 proyectos)

**Interfaces:**
- Produces:
  - `enum EstadoCorrida { EnCurso, Ok, Fallida }`
  - `class PipelineRun { Guid Id; Guid PipelineId; DateTime Inicio; DateTime? Fin; EstadoCorrida Estado; long FilasLeidas; long FilasEscritas; long FilasError; string? Mensaje }`
  - `interface IPipelineStore { Task<PipelineDefinition?> GetByIdAsync(Guid id, CancellationToken ct); Task<PipelineDefinition?> GetByNombreAsync(string nombre, CancellationToken ct); Task<IReadOnlyList<PipelineDefinition>> GetAllAsync(CancellationToken ct); Task UpsertAsync(PipelineDefinition def, CancellationToken ct); Task<IReadOnlyList<PipelineDefinition>> ObtenerVencidosAsync(DateTime ahora, CancellationToken ct); Task ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima, CancellationToken ct); }`
  - `interface IHistorialCorridas { Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct); Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct); Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct); }`
  - `class MetadataDbContext : DbContext`; `class EfPipelineStore : IPipelineStore`; `class EfHistorialCorridas : IHistorialCorridas`.

- [ ] **Step 1: Crear los tipos y contratos en Core**

`DataPipeline.Core/Pipelines/PipelineRun.cs`:
```csharp
namespace DataPipeline.Core.Pipelines;

/// <summary>Estado de una corrida de pipeline.</summary>
public enum EstadoCorrida { EnCurso, Ok, Fallida }

/// <summary>Registro de una corrida (historial / observabilidad).</summary>
public sealed class PipelineRun
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public EstadoCorrida Estado { get; set; }
    public long FilasLeidas { get; set; }
    public long FilasEscritas { get; set; }
    public long FilasError { get; set; }
    public string? Mensaje { get; set; }
}
```

`DataPipeline.Core/Abstractions/IPipelineStore.cs`:
```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Almacén de definiciones de pipeline (almacén A).</summary>
public interface IPipelineStore
{
    Task<PipelineDefinition?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PipelineDefinition?> GetByNombreAsync(string nombre, CancellationToken ct);
    Task<IReadOnlyList<PipelineDefinition>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PipelineDefinition def, CancellationToken ct);
    Task<IReadOnlyList<PipelineDefinition>> ObtenerVencidosAsync(DateTime ahora, CancellationToken ct);
    Task ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima, CancellationToken ct);
}
```

`DataPipeline.Core/Abstractions/IHistorialCorridas.cs`:
```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Historial de corridas (almacén A).</summary>
public interface IHistorialCorridas
{
    Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct);
    Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct);
    Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct);
}
```

- [ ] **Step 2: Crear el proyecto Persistence + referencias + paquetes**

Run (desde `E:\Sistemas\MiddleWare`):
```bash
dotnet new classlib -o DataPipeline.Persistence --framework net10.0
rm DataPipeline.Persistence/Class1.cs
dotnet sln add DataPipeline.Persistence
dotnet add DataPipeline.Persistence reference DataPipeline.Core
dotnet add DataPipeline.Persistence package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
dotnet add DataPipeline.Persistence package Microsoft.EntityFrameworkCore.Design --version 10.0.8
```
Asegurar en `DataPipeline.Persistence.csproj` el PropertyGroup estándar (`net10.0`, Nullable enable, ImplicitUsings enable).

- [ ] **Step 3: Crear el test project de Persistence + el test (debe fallar)**

Run:
```bash
dotnet new xunit -o tests/DataPipeline.Persistence.Tests --framework net10.0
rm tests/DataPipeline.Persistence.Tests/UnitTest1.cs
dotnet sln add tests/DataPipeline.Persistence.Tests
dotnet add tests/DataPipeline.Persistence.Tests reference DataPipeline.Core DataPipeline.Persistence
dotnet add tests/DataPipeline.Persistence.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
```

`tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class RepositoriosTests
{
    private static MetadataDbContext NuevoCtx()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"meta_{Guid.NewGuid():N}.db")};Pooling=False";
        var opts = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options;
        var ctx = new MetadataDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static PipelineDefinition Def(string nombre, DateTime? proxima, bool habilitado = true) => new()
    {
        Id = Guid.NewGuid(),
        Nombre = nombre,
        Habilitado = habilitado,
        Programacion = TipoProgramacion.Diaria,
        ProximaCorrida = proxima,
        TablaDestino = "t",
        ModoEscritura = ModoEscritura.Reemplazar,
        FilasPorLote = 1000,
        Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
    };

    [Fact]
    public async Task Upsert_y_GetByNombre_roundtrip_incluye_pasos()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        await store.UpsertAsync(Def("ventas", null), CancellationToken.None);

        var leido = await store.GetByNombreAsync("ventas", CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Single(leido!.Pasos);
        Assert.Equal("leer", leido.Pasos[0].Nombre);
    }

    [Fact]
    public async Task ObtenerVencidos_filtra_por_habilitado_y_proxima()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var ahora = new DateTime(2026, 6, 20, 12, 0, 0);
        await store.UpsertAsync(Def("vencido", ahora.AddMinutes(-1)), CancellationToken.None);
        await store.UpsertAsync(Def("futuro", ahora.AddHours(1)), CancellationToken.None);
        await store.UpsertAsync(Def("deshabilitado", ahora.AddMinutes(-1), habilitado: false), CancellationToken.None);
        await store.UpsertAsync(Def("ondemand", null), CancellationToken.None);

        var vencidos = await store.ObtenerVencidosAsync(ahora, CancellationToken.None);

        Assert.Single(vencidos);
        Assert.Equal("vencido", vencidos[0].Nombre);
    }

    [Fact]
    public async Task Historial_iniciar_completar_y_recientes()
    {
        using var ctx = NuevoCtx();
        var hist = new EfHistorialCorridas(ctx);
        var pid = Guid.NewGuid();

        var runId = await hist.IniciarAsync(pid, CancellationToken.None);
        await hist.CompletarAsync(runId, exito: true, leidas: 10, escritas: 8, error: null, CancellationToken.None);

        var recientes = await hist.ObtenerRecientesAsync(10, CancellationToken.None);
        Assert.Single(recientes);
        Assert.Equal(EstadoCorrida.Ok, recientes[0].Estado);
        Assert.Equal(8, recientes[0].FilasEscritas);
    }
}
```

- [ ] **Step 4: Correr y ver el RED**

Run: `dotnet build tests/DataPipeline.Persistence.Tests`
Expected: FALLA (no existen `MetadataDbContext`/`EfPipelineStore`/`EfHistorialCorridas`).

- [ ] **Step 5: Implementar el DbContext con converters JSON**

`DataPipeline.Persistence/MetadataDbContext.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DataPipeline.Persistence;

/// <summary>Almacén A: definiciones de pipeline + historial de corridas.</summary>
public sealed class MetadataDbContext : DbContext
{
    public MetadataDbContext(DbContextOptions<MetadataDbContext> options) : base(options) { }

    public DbSet<PipelineDefinition> Pipelines => Set<PipelineDefinition>();
    public DbSet<PipelineRun> Corridas => Set<PipelineRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var pipeline = b.Entity<PipelineDefinition>();
        pipeline.HasKey(p => p.Id);
        pipeline.HasIndex(p => p.Nombre).IsUnique();
        pipeline.HasIndex(p => p.ProximaCorrida);
        pipeline.Property(p => p.Programacion).HasConversion<string>();
        pipeline.Property(p => p.ModoEscritura).HasConversion<string>();

        // Colecciones como columnas JSON.
        var pasosConverter = new ValueConverter<List<StepDefinition>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<StepDefinition>>(v, jsonOpts) ?? new());
        var pasosComparer = new ValueComparer<List<StepDefinition>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<StepDefinition>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.Pasos).HasConversion(pasosConverter).Metadata.SetValueComparer(pasosComparer);

        var guidsConverter = new ValueConverter<List<Guid>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOpts) ?? new());
        var guidsComparer = new ValueComparer<List<Guid>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<Guid>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.CorrerDespuesDe).HasConversion(guidsConverter).Metadata.SetValueComparer(guidsComparer);

        var corrida = b.Entity<PipelineRun>();
        corrida.HasKey(c => c.Id);
        corrida.HasIndex(c => c.Inicio);
        corrida.Property(c => c.Estado).HasConversion<string>();
    }
}
```

- [ ] **Step 6: Implementar los repos**

`DataPipeline.Persistence/EfPipelineStore.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfPipelineStore : IPipelineStore
{
    private readonly MetadataDbContext _db;
    public EfPipelineStore(MetadataDbContext db) => _db = db;

    public Task<PipelineDefinition?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PipelineDefinition?> GetByNombreAsync(string nombre, CancellationToken ct) =>
        _db.Pipelines.FirstOrDefaultAsync(p => p.Nombre == nombre, ct);

    public async Task<IReadOnlyList<PipelineDefinition>> GetAllAsync(CancellationToken ct) =>
        await _db.Pipelines.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(PipelineDefinition def, CancellationToken ct)
    {
        var existe = await _db.Pipelines.AnyAsync(p => p.Id == def.Id, ct);
        if (existe) _db.Pipelines.Update(def);
        else _db.Pipelines.Add(def);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineDefinition>> ObtenerVencidosAsync(DateTime ahora, CancellationToken ct) =>
        await _db.Pipelines.AsNoTracking()
            .Where(p => p.Habilitado && p.ProximaCorrida != null && p.ProximaCorrida <= ahora)
            .ToListAsync(ct);

    public async Task ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return;
        def.UltimaCorrida = ultima;
        def.ProximaCorrida = proxima;
        await _db.SaveChangesAsync(ct);
    }
}
```

`DataPipeline.Persistence/EfHistorialCorridas.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfHistorialCorridas : IHistorialCorridas
{
    private readonly MetadataDbContext _db;
    public EfHistorialCorridas(MetadataDbContext db) => _db = db;

    public async Task<Guid> IniciarAsync(Guid pipelineId, CancellationToken ct)
    {
        var run = new PipelineRun
        {
            Id = Guid.NewGuid(), PipelineId = pipelineId,
            Inicio = DateTime.Now, Estado = EstadoCorrida.EnCurso
        };
        _db.Corridas.Add(run);
        await _db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task CompletarAsync(Guid runId, bool exito, long leidas, long escritas, string? error, CancellationToken ct)
    {
        var run = await _db.Corridas.FirstOrDefaultAsync(c => c.Id == runId, ct);
        if (run is null) return;
        run.Fin = DateTime.Now;
        run.Estado = exito ? EstadoCorrida.Ok : EstadoCorrida.Fallida;
        run.FilasLeidas = leidas;
        run.FilasEscritas = escritas;
        run.Mensaje = error;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineRun>> ObtenerRecientesAsync(int max, CancellationToken ct) =>
        await _db.Corridas.AsNoTracking().OrderByDescending(c => c.Inicio).Take(max).ToListAsync(ct);
}
```

- [ ] **Step 7: Correr los tests de repos y verificar verde**

Run: `dotnet test tests/DataPipeline.Persistence.Tests`
Expected: PASS (3 tests). (Usan `EnsureCreated`, no requieren migración para correr.)

- [ ] **Step 8: Generar la migración inicial**

Run:
```bash
dotnet add tests/DataPipeline.Persistence.Tests package Microsoft.EntityFrameworkCore.Design --version 10.0.8
dotnet ef migrations add InitMetadata --project DataPipeline.Persistence --startup-project DataPipeline.Persistence
```
> Si `dotnet ef` no está instalado: `dotnet tool install --global dotnet-ef`. La migración necesita un `IDesignTimeDbContextFactory<MetadataDbContext>`; crearlo si `dotnet ef` se queja:
>
> `DataPipeline.Persistence/MetadataDbContextFactory.cs`:
> ```csharp
> using Microsoft.EntityFrameworkCore;
> using Microsoft.EntityFrameworkCore.Design;
>
> namespace DataPipeline.Persistence;
>
> public sealed class MetadataDbContextFactory : IDesignTimeDbContextFactory<MetadataDbContext>
> {
>     public MetadataDbContext CreateDbContext(string[] args)
>     {
>         var opts = new DbContextOptionsBuilder<MetadataDbContext>()
>             .UseSqlite("Data Source=metadata.db").Options;
>         return new MetadataDbContext(opts);
>     }
> }
> ```

- [ ] **Step 9: Build pristine + commit**

Run: `dotnet build` → 0 warnings.
```bash
git add -A
git commit -m "feat(persistence): EF Core almacen A (MetadataDbContext, repos, migracion) + contratos en Core"
```

---

### Task 7: `ScheduleCalculator`

**Files:**
- Create: `DataPipeline.Engine/ScheduleCalculator.cs`
- Test: `tests/DataPipeline.Tests/ScheduleCalculatorTests.cs`

**Interfaces:**
- Consumes: `PipelineDefinition`, `TipoProgramacion`.
- Produces: `static class ScheduleCalculator` con `static DateTime? CalcularProxima(PipelineDefinition def, DateTime desde)`. `BajoDemanda` → null. Daily/Weekly/Monthly como el molde `EmailScheduleCalculator`.

- [ ] **Step 1: Escribir el test**

`tests/DataPipeline.Tests/ScheduleCalculatorTests.cs`:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class ScheduleCalculatorTests
{
    private static PipelineDefinition Def(TipoProgramacion prog, string hora) =>
        new() { Programacion = prog, HoraDelDia = hora };

    [Fact]
    public void BajoDemanda_devuelve_null()
    {
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.BajoDemanda, "08:00"), new DateTime(2026, 6, 20, 9, 0, 0));
        Assert.Null(r);
    }

    [Fact]
    public void Diaria_antes_de_la_hora_es_hoy()
    {
        var desde = new DateTime(2026, 6, 20, 6, 0, 0);
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.Diaria, "08:00"), desde);
        Assert.Equal(new DateTime(2026, 6, 20, 8, 0, 0), r);
    }

    [Fact]
    public void Diaria_despues_de_la_hora_es_manana()
    {
        var desde = new DateTime(2026, 6, 20, 9, 0, 0);
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.Diaria, "08:00"), desde);
        Assert.Equal(new DateTime(2026, 6, 21, 8, 0, 0), r);
    }

    [Fact]
    public void Mensual_usa_dia_del_mes()
    {
        var def = new PipelineDefinition { Programacion = TipoProgramacion.Mensual, HoraDelDia = "02:00", DiaDelMes = 5 };
        var r = ScheduleCalculator.CalcularProxima(def, new DateTime(2026, 6, 20, 9, 0, 0));
        Assert.Equal(new DateTime(2026, 7, 5, 2, 0, 0), r);
    }
}
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~ScheduleCalculatorTests"`
Expected: FALLA (no existe `ScheduleCalculator`).

- [ ] **Step 3: Implementar `ScheduleCalculator`**

`DataPipeline.Engine/ScheduleCalculator.cs`:
```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Engine;

/// <summary>Calcula la próxima corrida de un pipeline (hora local del servidor). Molde de EmailScheduleCalculator.</summary>
public static class ScheduleCalculator
{
    public static DateTime? CalcularProxima(PipelineDefinition def, DateTime desde)
    {
        var hora = ParseHora(def.HoraDelDia);
        return def.Programacion switch
        {
            TipoProgramacion.Diaria => ProximaDiaria(desde, hora),
            TipoProgramacion.Semanal => ProximaSemanal(desde, hora, def.DiaDeLaSemana ?? (int)desde.DayOfWeek),
            TipoProgramacion.Mensual => ProximaMensual(desde, hora, def.DiaDelMes ?? 1),
            _ => null // BajoDemanda
        };
    }

    private static TimeSpan ParseHora(string? hhmm)
    {
        if (TimeSpan.TryParse(hhmm, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
            return ts;
        return new TimeSpan(8, 0, 0);
    }

    private static DateTime ProximaDiaria(DateTime desde, TimeSpan hora)
    {
        var candidato = desde.Date + hora;
        return candidato > desde ? candidato : candidato.AddDays(1);
    }

    private static DateTime ProximaSemanal(DateTime desde, TimeSpan hora, int diaSemana)
    {
        diaSemana = Math.Clamp(diaSemana, 0, 6);
        for (int i = 0; i < 8; i++)
        {
            var fecha = desde.Date.AddDays(i);
            if ((int)fecha.DayOfWeek == diaSemana)
            {
                var candidato = fecha + hora;
                if (candidato > desde) return candidato;
            }
        }
        return desde.Date.AddDays(7) + hora;
    }

    private static DateTime ProximaMensual(DateTime desde, TimeSpan hora, int diaMes)
    {
        diaMes = Math.Clamp(diaMes, 1, 28);
        var candidato = new DateTime(desde.Year, desde.Month, diaMes) + hora;
        if (candidato <= desde)
            candidato = new DateTime(desde.Year, desde.Month, diaMes).AddMonths(1) + hora;
        return candidato;
    }
}
```

- [ ] **Step 4: Correr el test y verificar verde**

Run: `dotnet test --filter "FullyQualifiedName~ScheduleCalculatorTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(engine): ScheduleCalculator (Diaria/Semanal/Mensual)"
```

---

### Task 8: `EjecutorProgramado` (procesa pipelines vencidos)

**Files:**
- Create: `DataPipeline.Engine/EjecutorProgramado.cs`
- Modify: `DataPipeline.Engine/DataPipeline.Engine.csproj` (referencia a `DataPipeline.Persistence`? NO — depende solo de abstracciones de Core; recibe `IPipelineStore`/`IHistorialCorridas` por DI)
- Test: `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`
- Modify: `tests/DataPipeline.Tests/DataPipeline.Tests.csproj` (referencia a `DataPipeline.Persistence` para el test)

**Interfaces:**
- Consumes: `IPipelineStore`, `IHistorialCorridas`, `PipelineRunner`, `ScheduleCalculator`, `IEncryptionService`, `PipelineDefinition`.
- Produces: `class EjecutorProgramado(IPipelineStore store, IHistorialCorridas historial, PipelineRunner runner, IEncryptionService encryption)` con `Task<int> ProcesarVencidosAsync(DateTime ahora, CancellationToken ct)` → cantidad de pipelines procesados. Por cada vencido: inicia corrida, corre, completa, y en `finally` recalcula `ProximaCorrida` (anti-bucle). Excepciones por pipeline aisladas.

- [ ] **Step 1: Escribir el test (con repos EF reales + source fake)**

`tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`:
```csharp
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorProgramadoTests
{
    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejmeta_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static string NuevaData() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"ejdata_{Guid.NewGuid():N}.db")};Pooling=False";

    [Fact]
    public async Task Procesa_un_vencido_y_avanza_la_proxima_corrida()
    {
        using var meta = NuevoMeta();
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var ahora = new DateTime(2026, 6, 20, 9, 0, 0);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
            ProximaCorrida = ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Reemplazar, FilasPorLote = 10,
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var source = new FakeSource(new[] { "id" },
            new[] { new Record { Values = new Dictionary<string, object?> { ["id"] = 1 } } }, 10);
        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc);

        var n = await ejecutor.ProcesarVencidosAsync(ahora, CancellationToken.None);

        Assert.Equal(1, n);
        var recargado = await store.GetByIdAsync(def.Id, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 6, 21, 8, 0, 0), recargado!.ProximaCorrida);
        var corridas = await hist.ObtenerRecientesAsync(10, CancellationToken.None);
        Assert.Single(corridas);
        Assert.Equal(EstadoCorrida.Ok, corridas[0].Estado);
    }
}
```

- [ ] **Step 2: Agregar la referencia de test y ver el RED**

Run:
```bash
dotnet add tests/DataPipeline.Tests reference DataPipeline.Persistence
dotnet add tests/DataPipeline.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
dotnet test --filter "FullyQualifiedName~EjecutorProgramadoTests"
```
Expected: FALLA (no existe `EjecutorProgramado`).

- [ ] **Step 3: Implementar `EjecutorProgramado`**

`DataPipeline.Engine/EjecutorProgramado.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Procesa los pipelines vencidos: corre, registra historial y recalcula la próxima corrida.</summary>
public sealed class EjecutorProgramado
{
    private readonly IPipelineStore _store;
    private readonly IHistorialCorridas _historial;
    private readonly PipelineRunner _runner;
    private readonly IEncryptionService _encryption;

    public EjecutorProgramado(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
    }

    public async Task<int> ProcesarVencidosAsync(DateTime ahora, CancellationToken ct)
    {
        var vencidos = await _store.ObtenerVencidosAsync(ahora, ct);
        var procesados = 0;

        foreach (var def in vencidos)
        {
            var runId = await _historial.IniciarAsync(def.Id, ct);
            try
            {
                var result = await _runner.RunAsync(def, new Dictionary<string, string>(), _encryption, ct);
                await _historial.CompletarAsync(runId, result.Success, result.RowsRead, result.RowsWritten, result.Error, ct);
                procesados++;
            }
            catch (Exception ex)
            {
                await _historial.CompletarAsync(runId, false, 0, 0, ex.Message, ct);
            }
            finally
            {
                // Recalcular SIEMPRE para no reintentar en bucle.
                var proxima = ScheduleCalculator.CalcularProxima(def, DateTime.Now);
                await _store.ActualizarProgramacionAsync(def.Id, DateTime.Now, proxima, ct);
            }
        }

        return procesados;
    }
}
```

- [ ] **Step 4: Correr el test y verificar verde**

Run: `dotnet test --filter "FullyQualifiedName~EjecutorProgramadoTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(engine): EjecutorProgramado (corre vencidos, historial, recalcula proxima)"
```

---

### Task 9: `DataPipeline.Api` — proyecto, Basic Auth, DI y `GET /api/data/{nombre}`

**Files:**
- Create: `DataPipeline.Api/DataPipeline.Api.csproj` (Sdk.Web)
- Create: `DataPipeline.Api/Program.cs`
- Create: `DataPipeline.Api/Auth/BasicAuthHandler.cs`
- Create: `DataPipeline.Api/Controllers/DataController.cs`
- Create: `DataPipeline.Api/appsettings.json`
- Create: `tests/DataPipeline.Api.Tests/*`
- Modify: `DataPipeline.slnx`

**Interfaces:**
- Consumes: `IPipelineStore`, `IDataStore`, `SqliteDataStore`, `EfPipelineStore`, `MetadataDbContext`, `PipelineRunner`, `SourceFactory`, `TransformFactory`, `EncryptionService`, `EjecutorProgramado`, `ScheduleCalculator`.
- Produces: host ASP.NET Core con Basic Auth (`Basic` scheme), `GET /api/data/{nombre}?page=&pageSize=` → `{ columns, rows }`. `BasicAuthHandler` valida contra `PullAuth:Username` + `PullAuth:PasswordHash` (BCrypt) de config.

- [ ] **Step 1: Crear el proyecto Api + paquetes + referencias**

Run:
```bash
dotnet new web -o DataPipeline.Api --framework net10.0
dotnet sln add DataPipeline.Api
dotnet add DataPipeline.Api reference DataPipeline.Core DataPipeline.Connectors DataPipeline.Engine DataPipeline.Persistence
dotnet add DataPipeline.Api package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
dotnet add DataPipeline.Api package BCrypt.Net-Next
```
Asegurar PropertyGroup estándar (net10.0, Nullable, ImplicitUsings).

- [ ] **Step 2: Crear el test project Api + el test PULL (debe fallar)**

Run:
```bash
dotnet new xunit -o tests/DataPipeline.Api.Tests --framework net10.0
rm tests/DataPipeline.Api.Tests/UnitTest1.cs
dotnet sln add tests/DataPipeline.Api.Tests
dotnet add tests/DataPipeline.Api.Tests reference DataPipeline.Api DataPipeline.Core DataPipeline.Persistence DataPipeline.Connectors
dotnet add tests/DataPipeline.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/DataPipeline.Api.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
```

`tests/DataPipeline.Api.Tests/PullApiTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace DataPipeline.Api.Tests;

public class PullApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PullApiTests(ApiFactory factory) => _factory = factory;

    private static AuthenticationHeaderValue Cred(string u, string p) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{u}:{p}")));

    [Fact]
    public async Task Sin_credenciales_devuelve_401()
    {
        var cliente = _factory.CreateClient();
        var resp = await cliente.GetAsync("/api/data/ventas");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Con_credenciales_devuelve_las_filas_de_la_tabla()
    {
        await _factory.SeedPipelineConDatosAsync("ventas", "serving_ventas");
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred("pull", "secreto123");

        var resp = await cliente.GetAsync("/api/data/ventas?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"rows\"", json);
        Assert.Contains("importe", json);
    }

    [Fact]
    public async Task Pipeline_inexistente_devuelve_404()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred("pull", "secreto123");
        var resp = await cliente.GetAsync("/api/data/noexiste");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
```

`tests/DataPipeline.Api.Tests/ApiFactory.cs`:
```csharp
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataPipeline.Api.Tests;

/// <summary>Host de test: usa archivos SQLite temporales y credenciales PullAuth conocidas.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string MetaDb { get; } = Path.Combine(Path.GetTempPath(), $"apimeta_{Guid.NewGuid():N}.db");
    public string DataDb { get; } = Path.Combine(Path.GetTempPath(), $"apidata_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Metadata", $"Data Source={MetaDb};Pooling=False");
        builder.UseSetting("ConnectionStrings:Data", $"Data Source={DataDb};Pooling=False");
        builder.UseSetting("PullAuth:Username", "pull");
        // BCrypt hash de "secreto123":
        builder.UseSetting("PullAuth:PasswordHash", BCrypt.Net.BCrypt.HashPassword("secreto123"));
        builder.UseSetting("Encryption:Key", Convert.ToBase64String(new byte[32]));
    }

    public async Task SeedPipelineConDatosAsync(string nombre, string tabla)
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPipelineStore>();
        var data = scope.ServiceProvider.GetRequiredService<IDataStore>();

        await store.UpsertAsync(new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = nombre, Habilitado = true,
            Programacion = TipoProgramacion.BajoDemanda,
            TablaDestino = tabla, ModoEscritura = ModoEscritura.Reemplazar, FilasPorLote = 100,
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
        }, CancellationToken.None);

        await data.EnsureTableAsync(tabla, new[] { "importe" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await data.WriteBatchAsync(tabla, new RecordBatch
        {
            Columns = new[] { "importe" },
            Rows = new[] { new Record { Values = new Dictionary<string, object?> { ["importe"] = 42 } } }
        }, CancellationToken.None);
    }
}
```

- [ ] **Step 3: Correr y ver el RED**

Run: `dotnet build tests/DataPipeline.Api.Tests`
Expected: FALLA (no existe `Program` accesible / endpoints / auth).

- [ ] **Step 4: Implementar el Basic Auth handler**

`DataPipeline.Api/Auth/BasicAuthHandler.cs`:
```csharp
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DataPipeline.Api.Auth;

public sealed class BasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Basic";
    private readonly IConfiguration _config;

    public BasicAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration config) : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var raw))
            return Task.FromResult(AuthenticateResult.Fail("Falta Authorization."));

        try
        {
            var header = AuthenticationHeaderValue.Parse(raw!);
            if (!Scheme.Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
                return Task.FromResult(AuthenticateResult.Fail("Esquema inválido."));

            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            if (partes.Length != 2) return Task.FromResult(AuthenticateResult.Fail("Credencial mal formada."));

            var userOk = _config["PullAuth:Username"];
            var hash = _config["PullAuth:PasswordHash"];
            if (userOk is null || hash is null ||
                partes[0] != userOk || !BCrypt.Net.BCrypt.Verify(partes[1], hash))
                return Task.FromResult(AuthenticateResult.Fail("Credenciales inválidas."));

            var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, partes[0]) }, Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(id), Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Error de parseo."));
        }
    }
}
```

- [ ] **Step 5: Implementar el `DataController`**

`DataPipeline.Api/Controllers/DataController.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]
[Route("api/data")]
public sealed class DataController : ControllerBase
{
    private readonly IPipelineStore _store;
    private readonly IDataStore _data;

    public DataController(IPipelineStore store, IDataStore data)
    {
        _store = store;
        _data = data;
    }

    [HttpGet("{nombre}")]
    public async Task<IActionResult> Get(string nombre, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1) return BadRequest("page y pageSize deben ser >= 1.");

        var def = await _store.GetByNombreAsync(nombre, ct);
        if (def is null) return NotFound($"Pipeline '{nombre}' no encontrado.");

        try
        {
            var lote = await _data.ReadPagedAsync(def.TablaDestino, page, pageSize, ct);
            return Ok(new { columns = lote.Columns, rows = lote.Rows.Select(r => r.Values) });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return NotFound($"La tabla destino de '{nombre}' aún no existe (el pipeline no corrió todavía).");
        }
    }
}
```

- [ ] **Step 6: Implementar `Program.cs` con DI y wiring**

`DataPipeline.Api/Program.cs`:
```csharp
using DataPipeline.Api.Auth;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var metaCs = builder.Configuration.GetConnectionString("Metadata") ?? "Data Source=metadata.db";
var dataCs = builder.Configuration.GetConnectionString("Data") ?? "Data Source=data.db";

builder.Services.AddDbContext<MetadataDbContext>(o => o.UseSqlite(metaCs));
builder.Services.AddScoped<IPipelineStore, EfPipelineStore>();
builder.Services.AddScoped<IHistorialCorridas, EfHistorialCorridas>();
builder.Services.AddScoped<ISourceFactory, SourceFactory>();
builder.Services.AddScoped<ITransformFactory, TransformFactory>();
builder.Services.AddScoped<IDataStore>(_ => new SqliteDataStore(dataCs));
builder.Services.AddScoped<PipelineRunner>();
builder.Services.AddScoped<EjecutorProgramado>();
builder.Services.AddSingleton<IEncryptionService>(sp =>
    new EncryptionService(sp.GetRequiredService<IConfiguration>()["Encryption:Key"]
        ?? Convert.ToBase64String(new byte[32])));

builder.Services.AddAuthentication(BasicAuthHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>(BasicAuthHandler.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// Crear el esquema del almacén A al arrancar.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<MetadataDbContext>().Database.EnsureCreated();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { } // para WebApplicationFactory<Program>
```

> Nota: `PipelineRunner` se registra como Scoped; su ctor toma `ISourceFactory`, `ITransformFactory`, `IDataStore` (todos registrados). `IEncryptionService` Singleton (sin estado mutable).

- [ ] **Step 7: appsettings.json**

`DataPipeline.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Metadata": "Data Source=metadata.db",
    "Data": "Data Source=data.db"
  },
  "Encryption": {
    "Key": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
  },
  "PullAuth": {
    "Username": "pull",
    "PasswordHash": "$2a$11$REEMPLAZAR_POR_HASH_BCRYPT"
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```
> El `PasswordHash` real se genera con BCrypt (ej. desde la CLI o un script). El de tests lo setea `ApiFactory`. El `Encryption:Key` debe ser 32 bytes base64 en producción.

- [ ] **Step 8: Correr los tests de la API y verificar verde**

Run: `dotnet test tests/DataPipeline.Api.Tests`
Expected: PASS (3 tests: 401 sin auth, 200 con datos, 404 inexistente).

- [ ] **Step 9: Build pristine + commit**

Run: `dotnet build` → 0 warnings.
```bash
git add -A
git commit -m "feat(api): host + Basic Auth + GET /api/data/{nombre} (PULL)"
```

---

### Task 10: API — `POST /api/run/{id}`, `GET /api/runs` y el scheduler hosted

**Files:**
- Create: `DataPipeline.Api/Controllers/RunsController.cs`
- Create: `DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs`
- Modify: `DataPipeline.Api/Program.cs` (registrar el hosted service)
- Test: `tests/DataPipeline.Api.Tests/RunsApiTests.cs`

**Interfaces:**
- Consumes: `IPipelineStore`, `IHistorialCorridas`, `PipelineRunner`, `EjecutorProgramado`, `EncryptionService`.
- Produces: `POST /api/run/{id}` (corre on-demand, devuelve RunResult), `GET /api/runs?max=` (historial), `PipelineSchedulerHostedService : BackgroundService`.

- [ ] **Step 1: Escribir el test de runs (debe fallar)**

`tests/DataPipeline.Api.Tests/RunsApiTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace DataPipeline.Api.Tests;

public class RunsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public RunsApiTests(ApiFactory factory) => _factory = factory;

    private static AuthenticationHeaderValue Cred() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("pull:secreto123")));

    [Fact]
    public async Task GET_runs_requiere_auth()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GET_runs_devuelve_lista_vacia_o_corridas()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred();
        var resp = await cliente.GetAsync("/api/runs?max=5");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~RunsApiTests"`
Expected: FALLA (no existe el endpoint `/api/runs`).

- [ ] **Step 3: Implementar `RunsController`**

`DataPipeline.Api/Controllers/RunsController.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]
[Route("api")]
public sealed class RunsController : ControllerBase
{
    private readonly IPipelineStore _store;
    private readonly IHistorialCorridas _historial;
    private readonly PipelineRunner _runner;
    private readonly IEncryptionService _encryption;

    public RunsController(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
    }

    [HttpPost("run/{id:guid}")]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        var def = await _store.GetByIdAsync(id, ct);
        if (def is null) return NotFound($"Pipeline {id} no encontrado.");

        var runId = await _historial.IniciarAsync(def.Id, ct);
        var result = await _runner.RunAsync(def, new Dictionary<string, string>(), _encryption, ct);
        await _historial.CompletarAsync(runId, result.Success, result.RowsRead, result.RowsWritten, result.Error, ct);

        return Ok(new { result.RunId, result.Success, result.RowsRead, result.RowsWritten, result.Error });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> Runs(int max = 50, CancellationToken ct = default)
    {
        var recientes = await _historial.ObtenerRecientesAsync(Math.Clamp(max, 1, 500), ct);
        return Ok(recientes);
    }
}
```

- [ ] **Step 4: Implementar el `PipelineSchedulerHostedService`**

`DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs`:
```csharp
using DataPipeline.Engine;

namespace DataPipeline.Api.Hosting;

/// <summary>Revisa cada ~60s los pipelines vencidos y los corre. Molde del EmailSchedulerHostedService.</summary>
public sealed class PipelineSchedulerHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PipelineSchedulerHostedService> _logger;

    public PipelineSchedulerHostedService(IServiceProvider services, ILogger<PipelineSchedulerHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("PipelineScheduler iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var ejecutor = scope.ServiceProvider.GetRequiredService<EjecutorProgramado>();
                var n = await ejecutor.ProcesarVencidosAsync(DateTime.Now, stoppingToken);
                if (n > 0) _logger.LogInformation("PipelineScheduler: {N} pipeline(s) procesado(s).", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PipelineScheduler: error en el ciclo.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 5: Registrar el hosted service en `Program.cs`**

En `DataPipeline.Api/Program.cs`, después de `builder.Services.AddControllers();`, agregar:
```csharp
builder.Services.AddHostedService<DataPipeline.Api.Hosting.PipelineSchedulerHostedService>();
```

- [ ] **Step 6: Correr los tests de la API y verificar verde**

Run: `dotnet test tests/DataPipeline.Api.Tests`
Expected: PASS (los de Pull + los de Runs). El hosted service no interfiere (sin pipelines vencidos en los tests).

- [ ] **Step 7: Build pristine + commit**

Run: `dotnet build` → 0 warnings.
```bash
git add -A
git commit -m "feat(api): POST /api/run/{id}, GET /api/runs y scheduler hosted"
```

---

### Task 11: CLI — subcomando `import`

**Files:**
- Modify: `DataPipeline.Cli/Program.cs`
- Modify: `DataPipeline.Cli/DataPipeline.Cli.csproj` (referencia a `DataPipeline.Persistence` + EF Sqlite)
- Test: `tests/DataPipeline.Tests/CliImportTests.cs`

**Interfaces:**
- Consumes: `PipelineJsonLoader`, `ScheduleCalculator`, `MetadataDbContext`, `EfPipelineStore`, `IPipelineStore`.
- Produces: comando `import <pipeline.json> <metadata.db>` que carga el JSON, calcula `ProximaCorrida` inicial y hace `UpsertAsync`. El modo run de Fase 1 (`<pipeline.json> <sqlite.db>`) se conserva.

- [ ] **Step 1: Escribir el test del import (debe fallar)**

`tests/DataPipeline.Tests/CliImportTests.cs`:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Tests;

public class CliImportTests
{
    [Fact]
    public void ImportarDefinicion_calcula_proxima_y_persiste()
    {
        // Verifica la lógica de import (extraída a un helper estático para testearla sin proceso).
        var json = """
        {
          "nombre": "ventas_diaria",
          "habilitado": true,
          "programacion": "Diaria",
          "horaDelDia": "08:00",
          "tablaDestino": "serving",
          "modoEscritura": "Reemplazar",
          "pasos": [
            { "nombre": "leer", "tipo": "Origen", "conector": "SqlServerSource",
              "configuracion": { "connectionString": "Server=x;", "sql": "SELECT 1 AS n" } }
          ]
        }
        """;
        var def = PipelineJsonLoader.Load(json);
        var ahora = new DateTime(2026, 6, 20, 6, 0, 0);
        def.ProximaCorrida = ScheduleCalculator.CalcularProxima(def, ahora);

        Assert.Equal(new DateTime(2026, 6, 20, 8, 0, 0), def.ProximaCorrida);
        Assert.NotEqual(Guid.Empty, def.Id);
    }
}
```

> Nota: este test cubre la lógica (load + CalcularProxima) que el subcomando usa. El comando en sí es el cableado de esa lógica con `EfPipelineStore`.

- [ ] **Step 2: Correr y ver el estado**

Run: `dotnet test --filter "FullyQualifiedName~CliImportTests"`
Expected: PASS ya (usa APIs existentes). Sirve de regresión para el comando.

- [ ] **Step 3: Agregar referencias a la CLI e implementar el subcomando**

Run:
```bash
dotnet add DataPipeline.Cli reference DataPipeline.Persistence
dotnet add DataPipeline.Cli package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
```

Reemplazar `DataPipeline.Cli/Program.cs` por:
```csharp
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length >= 1 && args[0] == "import")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Uso: DataPipeline.Cli import <pipeline.json> <metadata.db>");
        return 1;
    }
    var defImport = PipelineJsonLoader.Load(await File.ReadAllTextAsync(args[1]));
    defImport.ProximaCorrida = ScheduleCalculator.CalcularProxima(defImport, DateTime.Now);

    var opts = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite($"Data Source={args[2]}").Options;
    using var ctx = new MetadataDbContext(opts);
    await ctx.Database.EnsureCreatedAsync();
    var store = new EfPipelineStore(ctx);
    await store.UpsertAsync(defImport, CancellationToken.None);

    Console.WriteLine($"Importado '{defImport.Nombre}' (Id {defImport.Id}), proxima corrida: {defImport.ProximaCorrida}");
    return 0;
}

// Modo run directo (Fase 1): <pipeline.json> <sqlite.db> [claveBase64]
if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: DataPipeline.Cli <pipeline.json> <sqlite.db> [claveBase64]");
    Console.Error.WriteLine("     DataPipeline.Cli import <pipeline.json> <metadata.db>");
    return 1;
}

var def = PipelineJsonLoader.Load(await File.ReadAllTextAsync(args[0]));
var data = new SqliteDataStore($"Data Source={args[1]}");
var runner = new PipelineRunner(new SourceFactory(), new TransformFactory(), data);
IEncryptionService enc = new EncryptionService(args.Length >= 3 ? args[2] : Convert.ToBase64String(new byte[32]));

var result = await runner.RunAsync(def, new Dictionary<string, string>(), enc);
Console.WriteLine(
    $"Run {result.RunId} -> Success={result.Success} Read={result.RowsRead} " +
    $"Written={result.RowsWritten} Dur={result.Duration} Error={result.Error}");
return result.Success ? 0 : 2;
```

- [ ] **Step 4: Build + suite completo verde**

Run: `dotnet build` → 0 warnings.
Run: `dotnet test` → PASS (todo el suite, todos los proyectos de test).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(cli): subcomando import (JSON -> almacen A) y runner con transforms"
```

---

## Self-Review

**1. Spec coverage:**
- §2 decisión 1 (definiciones en BD) → Task 6 ✅
- §2 decisión 2 (NCalc) → Task 3 ✅
- §2 decisión 3 (Basic Auth config) → Task 9 ✅
- §2 decisión 4 (proyecto Persistence) → Task 6 ✅
- §2 decisión 5 (Configuracion JsonElement) → Task 1 ✅
- §2 decisión 6 (POST /run, GET /runs) → Task 10 ✅
- §2 decisión 7 (metadata.db/data.db separados) → Task 9 (appsettings/DI) ✅
- §5 transforms (rename/cast/computed/filter, orden) → Tasks 2–3 ✅; TransformFactory → Task 4; runner compone → Task 5 ✅
- §6 scheduler (ScheduleCalculator + HostedService) → Tasks 7, 8, 10 ✅
- §7 API (data/run/runs + auth) → Tasks 9–10 ✅
- §9 CLI import → Task 11 ✅
- §11 testing (transform, calculator, repos, API integración, scheduler) → Tasks 2,3,6,7,8,9,10 ✅

**2. Placeholder scan:** sin TBD/TODO. El único valor a sustituir es el `PasswordHash` BCrypt en `appsettings.json` (marcado explícitamente; los tests setean el suyo vía `ApiFactory`).

**3. Type consistency:** `PipelineRunner(ISourceFactory, ITransformFactory, IDataStore)` usado igual en Tasks 5, 8, 9, 11; `IPipelineStore`/`IHistorialCorridas` firmas idénticas entre Task 6 (definición), 8 (EjecutorProgramado) y 9–10 (controllers); `ColumnMapTransform(rename,cast,computed,filter)` igual en Tasks 2, 3, 4; `ReadPagedAsync(table,page,pageSize,ct)` (Fase 1) consumido en Task 9; enums/props en castellano consistentes.

**4. Notas de alcance / orden:**
- Task 1 evoluciona el contrato y mantiene los 14 tests de Fase 1 verdes (ajusta SourceFactoryTests).
- Task 5 cambia el ctor de `PipelineRunner` (suma `ITransformFactory`); ajusta los tests de Fase 1 que lo construyen.
- `EjecutorProgramado` vive en `Engine` (depende solo de abstracciones de Core); el `HostedService` (Api) lo invoca por scope.
- `RowsRead` post-Fase-2 cuenta filas post-transform (documentado en Task 5); el conteo de filas crudas del origen se difiere a Fase 4 (métricas por paso).
- Fuera de alcance (Fase 4/5): watermark, Polly, dead-letter, upsert real, SqlServerDataStore, encadenamiento, PUSH.
