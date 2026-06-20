# DataPipeline — Fase 1 (Cimientos) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear la solución `DataPipeline` y correr un pipeline definido en JSON a mano que lee de SQL Server por streaming y aterriza en una tabla SQLite.

**Architecture:** Tres librerías (`Core` dominio puro, `Connectors` con el source en streaming + el data store SQLite, `Engine` con el runner) más una CLI mínima como harness de ejecución y un proyecto de tests xUnit. El dato viaja como `IAsyncEnumerable<RecordBatch>` (pull, memoria acotada); el runner conecta `Source → IDataStore` por lotes, sin transformaciones todavía.

**Tech Stack:** .NET 10 (`net10.0`), C# 13, `Microsoft.Data.SqlClient` (lectura), `Microsoft.Data.Sqlite` (escritura), xUnit (tests). Sin EF Core todavía (la BD de metadatos llega en Fase 2).

**Spec base:** `E:\Sistemas\MiddleWare\ANALISIS-MIDDLEWARE.md`, secciones 6–12 y 20 (Fase 1). Código de referencia a extraer: `E:\Sistemas\ConsultasGenericas\ConsultasGenericas.Api\Services\EncryptionService.cs` y `...\QueryExecutors\SqlServerQueryExecutor.cs`.

## Global Constraints

- TargetFramework: `net10.0`; `<Nullable>enable</Nullable>`; `<ImplicitUsings>enable</ImplicitUsings>` en todos los proyectos.
- `DataPipeline.Core` **no** referencia ningún driver ni paquete externo (solo BCL). Es el único proyecto sin dependencias.
- **Convención de idioma (Opción 2):** los **valores y tipos de enum** y las **propiedades** de los tipos de dominio/config (`PipelineDefinition`, `StepDefinition`) van **en castellano**; las **interfaces, los tipos técnicos de runtime** (`Record`, `RecordBatch`, `RunContext`, `RunResult`), los **servicios** (`PipelineRunner`, `SqliteDataStore`, etc.), los **métodos** y los **modismos .NET** (prefijo `I`, sufijo `Async`, `CancellationToken`) quedan **en inglés**. Variables locales y comentarios **en español**. Sin tildes ni `ñ` en identificadores.
- **Mapa de nombres (usar EXACTAMENTE estos):**
  - Enums: `TipoPaso { Origen, Transformacion }`; `TipoProgramacion { BajoDemanda, Diaria, Semanal, Mensual }`; `ModoEscritura { Agregar, Combinar, Reemplazar }`.
  - `PipelineDefinition` (tipo en inglés, props en castellano): `Id`, `Nombre`, `Habilitado`, `Programacion` (TipoProgramacion), `HoraDelDia` (string?), `DiaDeLaSemana` (int?), `DiaDelMes` (int?), `ProximaCorrida` (DateTime?), `UltimaCorrida` (DateTime?), `Pasos` (List<StepDefinition>), `TablaDestino` (string), `ModoEscritura` (ModoEscritura), `CorrerDespuesDe` (List<Guid>), `FilasPorLote` (int).
  - `StepDefinition` (tipo en inglés, props en castellano): `Nombre` (string), `Tipo` (TipoPaso), `Conector` (string), `Configuracion` (Dictionary<string,string>).
  - Quedan en inglés (sin cambios): `Record.Values`, `RecordBatch.Columns/Rows`, `RunContext.{PipelineId,RunId,Parameters,Encryption,BatchSize}`, `IEncryptionService.Encrypt/Decrypt`, todos los métodos `*Async`, `ISource/ITransform/IDataStore/ISourceFactory`.
  - JSON (camelCase de las props): `nombre`, `habilitado`, `programacion`, `horaDelDia`, `tablaDestino`, `modoEscritura`, `correrDespuesDe`, `filasPorLote`, `pasos`; y por paso: `nombre`, `tipo`, `conector`, `configuracion`.
- **Streaming obligatorio:** ningún source hace `ToList()`/`QueryAsync` del dataset completo. Solo `DbDataReader` + lotes.
- Cancelación cooperativa: todo método async acepta y propaga `CancellationToken`.
- Un commit como mínimo por tarea. El repo se inicializa en la Tarea 1.
- Identificadores SQL siempre entrecomillados con `"` para evitar inyección de nombres.

---

## File Structure

```
E:\Sistemas\MiddleWare\
├── .gitignore
├── nuget.config
├── DataPipeline.slnx
├── DataPipeline.Core/
│   ├── DataPipeline.Core.csproj
│   ├── Records/Record.cs
│   ├── Records/RecordBatch.cs
│   ├── Pipelines/PipelineDefinition.cs
│   ├── Pipelines/StepDefinition.cs
│   ├── Pipelines/Enums.cs            (TipoPaso, TipoProgramacion, ModoEscritura)
│   ├── Runtime/RunContext.cs
│   ├── Abstractions/ISource.cs
│   ├── Abstractions/ITransform.cs
│   ├── Abstractions/IDataStore.cs
│   ├── Abstractions/ISourceFactory.cs
│   └── Security/IEncryptionService.cs
├── DataPipeline.Connectors/
│   ├── DataPipeline.Connectors.csproj
│   ├── Security/EncryptionService.cs
│   ├── Sources/RecordBatcher.cs
│   ├── Sources/SqlServerSource.cs
│   ├── Stores/SqliteDataStore.cs
│   └── Factories/SourceFactory.cs
├── DataPipeline.Engine/
│   ├── DataPipeline.Engine.csproj
│   ├── RunResult.cs
│   ├── PipelineRunner.cs
│   └── PipelineJsonLoader.cs
├── DataPipeline.Cli/
│   ├── DataPipeline.Cli.csproj
│   └── Program.cs
└── tests/
    └── DataPipeline.Tests/
        ├── DataPipeline.Tests.csproj
        ├── EncryptionServiceTests.cs
        ├── RecordBatcherTests.cs
        ├── SqliteDataStoreTests.cs
        ├── PipelineRunnerTests.cs
        ├── PipelineJsonLoaderTests.cs
        └── Fakes/FakeSource.cs (+ FakeSourceFactory)
```

---

### Task 1: Scaffold de la solución y proyectos

**Files:**
- Create: `E:\Sistemas\MiddleWare\nuget.config`
- Create: `E:\Sistemas\MiddleWare\.gitignore`
- Create: `E:\Sistemas\MiddleWare\DataPipeline.slnx` (vía `dotnet`)
- Create: los 5 `.csproj` (Core, Connectors, Engine, Cli, tests)

**Interfaces:**
- Consumes: nada.
- Produces: una solución que compila vacía y referencias entre proyectos: `Connectors→Core`, `Engine→Core+Connectors`, `Cli→Engine+Connectors+Core`, `Tests→Core+Connectors+Engine`.

- [ ] **Step 1: Crear `nuget.config`**

`E:\Sistemas\MiddleWare\nuget.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- [ ] **Step 2: Crear `.gitignore`**

`E:\Sistemas\MiddleWare\.gitignore`:
```gitignore
bin/
obj/
*.user
*.db
*.db-shm
*.db-wal
.vs/
```
> No ignorar `ANALISIS-MIDDLEWARE.md` ni `docs/`.

- [ ] **Step 3: Crear proyectos y solución**

Run (desde `E:\Sistemas\MiddleWare`):
```bash
dotnet new sln --name DataPipeline --format slnx
dotnet new classlib -o DataPipeline.Core --framework net10.0
dotnet new classlib -o DataPipeline.Connectors --framework net10.0
dotnet new classlib -o DataPipeline.Engine --framework net10.0
dotnet new console  -o DataPipeline.Cli --framework net10.0
dotnet new xunit    -o tests/DataPipeline.Tests --framework net10.0
```
> Si `--format slnx` no estuviera disponible en este SDK, usar `dotnet new sln --name DataPipeline` (genera `.sln` clásico); el resto del plan funciona igual.

- [ ] **Step 4: Borrar los `Class1.cs` generados y agregar referencias + proyectos a la solución**

Run:
```bash
rm DataPipeline.Core/Class1.cs DataPipeline.Connectors/Class1.cs DataPipeline.Engine/Class1.cs

dotnet sln add DataPipeline.Core DataPipeline.Connectors DataPipeline.Engine DataPipeline.Cli tests/DataPipeline.Tests

dotnet add DataPipeline.Connectors reference DataPipeline.Core
dotnet add DataPipeline.Engine reference DataPipeline.Core DataPipeline.Connectors
dotnet add DataPipeline.Cli reference DataPipeline.Engine DataPipeline.Connectors DataPipeline.Core
dotnet add tests/DataPipeline.Tests reference DataPipeline.Core DataPipeline.Connectors DataPipeline.Engine
```

- [ ] **Step 5: Activar Nullable + ImplicitUsings donde falte**

Editar `DataPipeline.Core/DataPipeline.Core.csproj`, `DataPipeline.Connectors/...csproj` y `DataPipeline.Engine/...csproj` para que el `<PropertyGroup>` contenga exactamente:
```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
```
(Los proyectos de `console` y `xunit` ya vienen con estas opciones; verificar y dejarlas igual.)

- [ ] **Step 6: Compilar la solución vacía**

Run: `dotnet build`
Expected: `Build succeeded` con 0 errores (puede haber 0 warnings).

- [ ] **Step 7: Inicializar git y commit**

Run:
```bash
git init
git add -A
git commit -m "chore: scaffold solución DataPipeline (Core/Connectors/Engine/Cli/Tests)"
```

---

### Task 2: Modelo de dominio en Core

**Files:**
- Create: `DataPipeline.Core/Records/Record.cs`, `RecordBatch.cs`
- Create: `DataPipeline.Core/Pipelines/Enums.cs`, `StepDefinition.cs`, `PipelineDefinition.cs`
- Create: `DataPipeline.Core/Runtime/RunContext.cs`
- Create: `DataPipeline.Core/Security/IEncryptionService.cs`
- Create: `DataPipeline.Core/Abstractions/ISource.cs`, `ITransform.cs`, `IDataStore.cs`, `ISourceFactory.cs`
- Test: ninguno en esta tarea. Estos tipos son POCOs/contratos puros; su cobertura real llega con el loader y el runner (Tareas 4 y 7, que crean sus propios archivos de test). La verificación de la Tarea 2 es `dotnet build`.

**Interfaces:**
- Consumes: nada.
- Produces (firmas que el resto del plan usa textualmente):
  - `record Record { IReadOnlyDictionary<string, object?> Values }`
  - `record RecordBatch { IReadOnlyList<string> Columns; IReadOnlyList<Record> Rows }`
  - `enum TipoPaso { Origen, Transformacion }`, `enum TipoProgramacion { BajoDemanda, Diaria, Semanal, Mensual }`, `enum ModoEscritura { Agregar, Combinar, Reemplazar }`
  - `class StepDefinition { string Nombre; TipoPaso Tipo; string Conector; Dictionary<string,string> Configuracion }`
  - `class PipelineDefinition { Guid Id; string Nombre; bool Habilitado; TipoProgramacion Programacion; string? HoraDelDia; int? DiaDeLaSemana; int? DiaDelMes; DateTime? ProximaCorrida; DateTime? UltimaCorrida; List<StepDefinition> Pasos; string TablaDestino; ModoEscritura ModoEscritura; List<Guid> CorrerDespuesDe; int FilasPorLote }`
  - `class RunContext { Guid PipelineId; Guid RunId; IReadOnlyDictionary<string,string> Parameters; IEncryptionService Encryption; int BatchSize }`
  - `interface IEncryptionService { string Encrypt(string text); string Decrypt(string encryptedText); }`
  - `interface ISource { IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, CancellationToken ct); }`
  - `interface ITransform { IAsyncEnumerable<RecordBatch> TransformAsync(IAsyncEnumerable<RecordBatch> input, RunContext ctx, CancellationToken ct); }`
  - `interface IDataStore { Task EnsureTableAsync(string table, IReadOnlyList<string> columns, ModoEscritura mode, CancellationToken ct); Task WriteBatchAsync(string table, RecordBatch batch, CancellationToken ct); Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, CancellationToken ct); }`
  - `interface ISourceFactory { ISource Create(StepDefinition step); }`

- [ ] **Step 1: Crear los tipos de datos en vuelo**

`DataPipeline.Core/Records/Record.cs`:
```csharp
namespace DataPipeline.Core.Records;

/// <summary>Un registro: diccionario columna→valor.</summary>
public sealed record Record
{
    public required IReadOnlyDictionary<string, object?> Values { get; init; }
}
```

`DataPipeline.Core/Records/RecordBatch.cs`:
```csharp
namespace DataPipeline.Core.Records;

/// <summary>Un lote de registros que viaja entre pasos del pipeline.</summary>
public sealed record RecordBatch
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<Record> Rows { get; init; }
}
```

- [ ] **Step 2: Crear los enums y las definiciones de pipeline**

`DataPipeline.Core/Pipelines/Enums.cs`:
```csharp
namespace DataPipeline.Core.Pipelines;

/// <summary>Clase de un paso: lee de un origen o transforma datos en vuelo.</summary>
public enum TipoPaso { Origen, Transformacion }

/// <summary>Forma de programar un pipeline.</summary>
public enum TipoProgramacion { BajoDemanda, Diaria, Semanal, Mensual }

/// <summary>Cómo escribe el sink interno en la tabla destino.</summary>
public enum ModoEscritura { Agregar, Combinar, Reemplazar }
```

`DataPipeline.Core/Pipelines/StepDefinition.cs`:
```csharp
namespace DataPipeline.Core.Pipelines;

/// <summary>Un paso del pipeline (Origen o Transformación). El sink interno es implícito.</summary>
public sealed class StepDefinition
{
    public string Nombre { get; set; } = "";
    public TipoPaso Tipo { get; set; }
    public string Conector { get; set; } = "";
    public Dictionary<string, string> Configuracion { get; set; } = new();
}
```

`DataPipeline.Core/Pipelines/PipelineDefinition.cs`:
```csharp
namespace DataPipeline.Core.Pipelines;

/// <summary>Definición declarativa de un pipeline (lo que se persiste/edita).</summary>
public sealed class PipelineDefinition
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Habilitado { get; set; } = true;

    public TipoProgramacion Programacion { get; set; } = TipoProgramacion.BajoDemanda;
    public string? HoraDelDia { get; set; }
    public int? DiaDeLaSemana { get; set; }
    public int? DiaDelMes { get; set; }
    public DateTime? ProximaCorrida { get; set; }
    public DateTime? UltimaCorrida { get; set; }

    public List<StepDefinition> Pasos { get; set; } = new();
    public string TablaDestino { get; set; } = "";
    public ModoEscritura ModoEscritura { get; set; } = ModoEscritura.Reemplazar;

    public List<Guid> CorrerDespuesDe { get; set; } = new();

    public int FilasPorLote { get; set; } = 5_000;
}
```

- [ ] **Step 3: Crear el contrato de encriptación**

`DataPipeline.Core/Security/IEncryptionService.cs`:
```csharp
namespace DataPipeline.Core.Security;

/// <summary>Contrato de encriptación AES-256 (idéntico al de ConsultasGenericas).</summary>
public interface IEncryptionService
{
    string Encrypt(string text);
    string Decrypt(string encryptedText);
}
```

- [ ] **Step 4: Crear el RunContext**

`DataPipeline.Core/Runtime/RunContext.cs`:
```csharp
using DataPipeline.Core.Security;

namespace DataPipeline.Core.Runtime;

/// <summary>Contexto de una corrida: identidad, parámetros, secretos y tamaño de lote.</summary>
public sealed class RunContext
{
    public required Guid PipelineId { get; init; }
    public required Guid RunId { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required IEncryptionService Encryption { get; init; }
    public int BatchSize { get; init; } = 5_000;
}
```

- [ ] **Step 5: Crear los contratos de pasos y la fábrica de sources**

`DataPipeline.Core/Abstractions/ISource.cs`:
```csharp
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Core.Abstractions;

/// <summary>Lee del origen en lotes, sin materializar todo en memoria.</summary>
public interface ISource
{
    IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, CancellationToken ct);
}
```

`DataPipeline.Core/Abstractions/ITransform.cs`:
```csharp
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Core.Abstractions;

/// <summary>Transforma lote a lote. (Sin implementaciones en Fase 1.)</summary>
public interface ITransform
{
    IAsyncEnumerable<RecordBatch> TransformAsync(
        IAsyncEnumerable<RecordBatch> input, RunContext ctx, CancellationToken ct);
}
```

`DataPipeline.Core/Abstractions/IDataStore.cs`:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Sink interno + lectura para la entrega. Aísla el motor de la BD concreta.</summary>
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columns, ModoEscritura mode, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, CancellationToken ct);
}
```

`DataPipeline.Core/Abstractions/ISourceFactory.cs`:
```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve un StepDefinition de tipo Source a su conector concreto.</summary>
public interface ISourceFactory
{
    ISource Create(StepDefinition step);
}
```

- [ ] **Step 6: Compilar Core**

Run: `dotnet build DataPipeline.Core`
Expected: `Build succeeded`, 0 errores.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): modelo de dominio, contratos y RunContext"
```

---

### Task 3: EncryptionService (extraído de ConsultasGenericas)

**Files:**
- Create: `DataPipeline.Connectors/Security/EncryptionService.cs`
- Test: `tests/DataPipeline.Tests/EncryptionServiceTests.cs`

**Interfaces:**
- Consumes: `IEncryptionService` (Task 2).
- Produces: `class EncryptionService : IEncryptionService` con constructor `EncryptionService(string claveBase64)` (clave de 32 bytes en base64).

- [ ] **Step 1: Escribir el test que falla**

`tests/DataPipeline.Tests/EncryptionServiceTests.cs`:
```csharp
using DataPipeline.Connectors.Security;
using Xunit;

namespace DataPipeline.Tests;

public class EncryptionServiceTests
{
    private static string ClaveValida() => Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Encrypt_luego_Decrypt_devuelve_el_texto_original()
    {
        var svc = new EncryptionService(ClaveValida());
        var original = "Server=POSSERVER;Database=ventas;User Id=sa;Password=secreto;";

        var cifrado = svc.Encrypt(original);
        var descifrado = svc.Decrypt(cifrado);

        Assert.NotEqual(original, cifrado);
        Assert.Equal(original, descifrado);
    }

    [Fact]
    public void Constructor_con_clave_de_largo_invalido_lanza()
    {
        var claveCorta = Convert.ToBase64String(new byte[16]);
        Assert.Throws<InvalidOperationException>(() => new EncryptionService(claveCorta));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~EncryptionServiceTests"`
Expected: FAIL de compilación ("EncryptionService no existe" / type or namespace not found).

- [ ] **Step 3: Implementar el servicio**

`DataPipeline.Connectors/Security/EncryptionService.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using DataPipeline.Core.Security;

namespace DataPipeline.Connectors.Security;

/// <summary>Encriptación AES-256 CBC con IV aleatorio. Clave de 32 bytes en base64.</summary>
public sealed class EncryptionService : IEncryptionService
{
    private readonly byte[] _clave;

    public EncryptionService(string claveBase64)
    {
        if (string.IsNullOrWhiteSpace(claveBase64))
            throw new ArgumentException("La clave de encriptación es obligatoria.", nameof(claveBase64));

        _clave = Convert.FromBase64String(claveBase64);

        if (_clave.Length != 32)
            throw new InvalidOperationException("La clave debe tener exactamente 32 bytes (AES-256).");
    }

    public string Encrypt(string text)
    {
        using var aes = Aes.Create();
        aes.Key = _clave;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(text);
        var cifrado = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);

        var resultado = new byte[aes.IV.Length + cifrado.Length];
        Array.Copy(aes.IV, 0, resultado, 0, aes.IV.Length);
        Array.Copy(cifrado, 0, resultado, aes.IV.Length, cifrado.Length);
        return Convert.ToBase64String(resultado);
    }

    public string Decrypt(string encryptedText)
    {
        var datos = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = _clave;

        var iv = new byte[aes.BlockSize / 8];
        var cifrado = new byte[datos.Length - iv.Length];
        Array.Copy(datos, 0, iv, 0, iv.Length);
        Array.Copy(datos, iv.Length, cifrado, 0, cifrado.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var bytes = decryptor.TransformFinalBlock(cifrado, 0, cifrado.Length);
        return Encoding.UTF8.GetString(bytes);
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test --filter "FullyQualifiedName~EncryptionServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(connectors): EncryptionService AES-256 extraído de ConsultasGenericas"
```

---

### Task 4: SqliteDataStore

**Files:**
- Create: `DataPipeline.Connectors/Stores/SqliteDataStore.cs`
- Modify: `DataPipeline.Connectors/DataPipeline.Connectors.csproj` (agregar `Microsoft.Data.Sqlite`)
- Test: `tests/DataPipeline.Tests/SqliteDataStoreTests.cs`
- Modify: `tests/DataPipeline.Tests/DataPipeline.Tests.csproj` (agregar `Microsoft.Data.Sqlite` para los tests)

**Interfaces:**
- Consumes: `IDataStore`, `RecordBatch`, `Record`, `ModoEscritura` (Task 2).
- Produces: `class SqliteDataStore : IDataStore` con constructor `SqliteDataStore(string connectionString)` (ej. `"Data Source=ruta.db"`).

- [ ] **Step 1: Agregar el paquete Microsoft.Data.Sqlite**

Run:
```bash
dotnet add DataPipeline.Connectors package Microsoft.Data.Sqlite --version 10.0.8
dotnet add tests/DataPipeline.Tests package Microsoft.Data.Sqlite --version 10.0.8
```

- [ ] **Step 2: Escribir el test que falla**

`tests/DataPipeline.Tests/SqliteDataStoreTests.cs`:
```csharp
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Xunit;

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
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~SqliteDataStoreTests"`
Expected: FAIL de compilación ("SqliteDataStore no existe").

- [ ] **Step 4: Implementar el data store**

`DataPipeline.Connectors/Stores/SqliteDataStore.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Microsoft.Data.Sqlite;

namespace DataPipeline.Connectors.Stores;

/// <summary>IDataStore sobre SQLite. Tablas de tipado dinámico (sin tipos declarados).</summary>
public sealed class SqliteDataStore : IDataStore
{
    private readonly string _connectionString;

    public SqliteDataStore(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string table, IReadOnlyList<string> columns, ModoEscritura mode, CancellationToken ct)
    {
        if (mode == ModoEscritura.Combinar)
            throw new NotSupportedException("ModoEscritura.Combinar (upsert) se implementa en Fase 4.");
        if (columns.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columns));

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        if (mode == ModoEscritura.Reemplazar)
        {
            await using var drop = con.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {Quote(table)};";
            await drop.ExecuteNonQueryAsync(ct);
        }

        var defs = string.Join(", ", columns.Select(Quote));
        await using var crear = con.CreateCommand();
        crear.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({defs});";
        await crear.ExecuteNonQueryAsync(ct);
    }

    public async Task WriteBatchAsync(string table, RecordBatch batch, CancellationToken ct)
    {
        if (batch.Rows.Count == 0) return;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await con.BeginTransactionAsync(ct);

        var cols = batch.Columns;
        var listaCols = string.Join(", ", cols.Select(Quote));
        var listaParams = string.Join(", ", cols.Select((_, i) => "$p" + i));

        await using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {Quote(table)} ({listaCols}) VALUES ({listaParams});";

        var parms = new SqliteParameter[cols.Count];
        for (int i = 0; i < cols.Count; i++)
        {
            parms[i] = cmd.CreateParameter();
            parms[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(parms[i]);
        }

        foreach (var fila in batch.Rows)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                fila.Values.TryGetValue(cols[i], out var valor);
                parms[i].Value = valor ?? DBNull.Value;
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} LIMIT $take OFFSET $skip;";
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

    private static string Quote(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test --filter "FullyQualifiedName~SqliteDataStoreTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(connectors): SqliteDataStore (Append/Replace + ReadPaged)"
```

---

### Task 5: RecordBatcher + SqlServerSource

**Files:**
- Create: `DataPipeline.Connectors/Sources/RecordBatcher.cs`
- Create: `DataPipeline.Connectors/Sources/SqlServerSource.cs`
- Modify: `DataPipeline.Connectors/DataPipeline.Connectors.csproj` (agregar `Microsoft.Data.SqlClient`)
- Test: `tests/DataPipeline.Tests/RecordBatcherTests.cs`

> El `SqlServerSource` es un adaptador fino que cablea `DbDataReader` al `RecordBatcher`. La lógica testeable (el loteo) se prueba en `RecordBatcher`; el `SqlServerSource` se verifica end-to-end contra SQL Server real en la Tarea 8 (necesita una BD).

**Interfaces:**
- Consumes: `Record`, `RecordBatch`, `ISource`, `RunContext` (Task 2).
- Produces:
  - `static class RecordBatcher` con `static IAsyncEnumerable<RecordBatch> Batch(IReadOnlyList<string> columnas, IAsyncEnumerable<Record> filas, int tamanoLote, CancellationToken ct)`
  - `class SqlServerSource : ISource` con constructor `SqlServerSource(string connectionString, string sql, bool connectionStringEncrypted)`

- [ ] **Step 1: Escribir el test que falla (RecordBatcher)**

`tests/DataPipeline.Tests/RecordBatcherTests.cs`:
```csharp
using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using Xunit;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class RecordBatcherTests
{
    private static async IAsyncEnumerable<Record> Generar(int cantidad, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < cantidad; i++)
        {
            await Task.Yield();
            yield return new Record { Values = new Dictionary<string, object?> { ["n"] = i } };
        }
    }

    [Fact]
    public async Task Agrupa_en_lotes_del_tamano_pedido_con_resto_final()
    {
        var cols = new[] { "n" };
        var lotes = new List<int>();
        await foreach (var lote in RecordBatcher.Batch(cols, Generar(7), tamanoLote: 3, CancellationToken.None))
            lotes.Add(lote.Rows.Count);

        Assert.Equal(new[] { 3, 3, 1 }, lotes);
    }

    [Fact]
    public async Task Secuencia_vacia_no_emite_lotes()
    {
        var cols = new[] { "n" };
        var lotes = 0;
        await foreach (var _ in RecordBatcher.Batch(cols, Generar(0), 5, CancellationToken.None))
            lotes++;

        Assert.Equal(0, lotes);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~RecordBatcherTests"`
Expected: FAIL de compilación ("RecordBatcher no existe").

- [ ] **Step 3: Implementar el RecordBatcher**

`DataPipeline.Connectors/Sources/RecordBatcher.cs`:
```csharp
using System.Runtime.CompilerServices;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Sources;

/// <summary>Agrupa una secuencia perezosa de Records en RecordBatch de tamaño fijo.</summary>
public static class RecordBatcher
{
    public static async IAsyncEnumerable<RecordBatch> Batch(
        IReadOnlyList<string> columnas,
        IAsyncEnumerable<Record> filas,
        int tamanoLote,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (tamanoLote <= 0) tamanoLote = 5_000;

        var buffer = new List<Record>(tamanoLote);
        await foreach (var fila in filas.WithCancellation(ct))
        {
            buffer.Add(fila);
            if (buffer.Count >= tamanoLote)
            {
                yield return new RecordBatch { Columns = columnas, Rows = buffer };
                buffer = new List<Record>(tamanoLote);
            }
        }
        if (buffer.Count > 0)
            yield return new RecordBatch { Columns = columnas, Rows = buffer };
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test --filter "FullyQualifiedName~RecordBatcherTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Agregar Microsoft.Data.SqlClient e implementar el SqlServerSource**

Run:
```bash
dotnet add DataPipeline.Connectors package Microsoft.Data.SqlClient
```

`DataPipeline.Connectors/Sources/SqlServerSource.cs`:
```csharp
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Sources;

/// <summary>Source de SQL Server en streaming (DbDataReader + lotes). Nunca materializa todo.</summary>
public sealed class SqlServerSource : ISource
{
    private readonly string _connectionStringRaw;
    private readonly string _sql;
    private readonly bool _encriptada;

    public SqlServerSource(string connectionString, string sql, bool connectionStringEncrypted)
    {
        _connectionStringRaw = connectionString;
        _sql = sql;
        _encriptada = connectionStringEncrypted;
    }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var cs = _encriptada ? ctx.Encryption.Decrypt(_connectionStringRaw) : _connectionStringRaw;

        await using var con = new SqlConnection(cs);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(_sql, con) { CommandTimeout = 0 };
        foreach (var (clave, valor) in ctx.Parameters)
            cmd.Parameters.AddWithValue("@" + clave.TrimStart('@'), (object?)valor ?? DBNull.Value);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();

        await foreach (var lote in RecordBatcher.Batch(cols, LeerFilas(rd, cols, ct), ctx.BatchSize, ct))
            yield return lote;
    }

    private static async IAsyncEnumerable<Record> LeerFilas(
        SqlDataReader rd, string[] cols, [EnumeratorCancellation] CancellationToken ct)
    {
        while (await rd.ReadAsync(ct))
        {
            var vals = new Dictionary<string, object?>(cols.Length);
            for (int i = 0; i < cols.Length; i++)
                vals[cols[i]] = await rd.IsDBNullAsync(i, ct) ? null : rd.GetValue(i);
            yield return new Record { Values = vals };
        }
    }
}
```

- [ ] **Step 6: Compilar Connectors**

Run: `dotnet build DataPipeline.Connectors`
Expected: `Build succeeded`, 0 errores.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(connectors): RecordBatcher + SqlServerSource en streaming"
```

---

### Task 6: SourceFactory

**Files:**
- Create: `DataPipeline.Connectors/Factories/SourceFactory.cs`
- Test: cubierto indirectamente por `PipelineRunnerTests` (Task 7) vía `FakeSourceFactory`; acá un test directo de resolución.
- Test: `tests/DataPipeline.Tests/SourceFactoryTests.cs`

**Interfaces:**
- Consumes: `ISourceFactory`, `StepDefinition`, `TipoPaso`, `SqlServerSource` (Tasks 2, 5).
- Produces: `class SourceFactory : ISourceFactory`.

- [ ] **Step 1: Escribir el test que falla**

`tests/DataPipeline.Tests/SourceFactoryTests.cs`:
```csharp
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class SourceFactoryTests
{
    [Fact]
    public void Crea_un_SqlServerSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer",
            Tipo = TipoPaso.Origen,
            Conector = "SqlServerSource",
            Configuracion = new() { ["connectionString"] = "Server=x;", ["sql"] = "SELECT 1" }
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

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~SourceFactoryTests"`
Expected: FAIL de compilación ("SourceFactory no existe").

- [ ] **Step 3: Implementar la fábrica**

`DataPipeline.Connectors/Factories/SourceFactory.cs`:
```csharp
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Source a su conector concreto (Fase 1: SqlServerSource).</summary>
public sealed class SourceFactory : ISourceFactory
{
    public ISource Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Origen)
            throw new ArgumentException($"El paso '{step.Nombre}' no es un Origen.", nameof(step));

        return step.Conector switch
        {
            "SqlServerSource" => new SqlServerSource(
                Requerido(step, "connectionString"),
                Requerido(step, "sql"),
                step.Configuracion.TryGetValue("connectionStringEncrypted", out var e)
                    && bool.TryParse(e, out var b) && b),
            _ => throw new NotSupportedException($"Conector de origen no soportado en Fase 1: '{step.Conector}'.")
        };
    }

    private static string Requerido(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"El origen '{step.Nombre}' requiere la config '{clave}'.", nameof(step));
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test --filter "FullyQualifiedName~SourceFactoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(connectors): SourceFactory resuelve SqlServerSource por tipo"
```

---

### Task 7: PipelineRunner + PipelineJsonLoader

**Files:**
- Create: `DataPipeline.Engine/RunResult.cs`
- Create: `DataPipeline.Engine/PipelineRunner.cs`
- Create: `DataPipeline.Engine/PipelineJsonLoader.cs`
- Create: `tests/DataPipeline.Tests/Fakes/FakeSource.cs` (incluye `FakeSourceFactory`)
- Test: `tests/DataPipeline.Tests/PipelineRunnerTests.cs`
- Test: `tests/DataPipeline.Tests/PipelineJsonLoaderTests.cs`

**Interfaces:**
- Consumes: `ISourceFactory`, `IDataStore`, `IEncryptionService`, `PipelineDefinition`, `TipoPaso`, `RunContext`, `RecordBatch`, `SqliteDataStore` (Tasks 2, 4).
- Produces:
  - `record RunResult(Guid RunId, bool Success, long RowsRead, long RowsWritten, string? Error, TimeSpan Duration)`
  - `class PipelineRunner(ISourceFactory sourceFactory, IDataStore dataStore)` con `Task<RunResult> RunAsync(PipelineDefinition def, IReadOnlyDictionary<string,string> parameters, IEncryptionService encryption, CancellationToken ct = default)`
  - `static class PipelineJsonLoader` con `static PipelineDefinition Load(string json)`

- [ ] **Step 1: Crear los fakes de test**

`tests/DataPipeline.Tests/Fakes/FakeSource.cs`:
```csharp
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests.Fakes;

/// <summary>Source de prueba que emite filas predefinidas en lotes de tamaño fijo.</summary>
internal sealed class FakeSource : ISource
{
    private readonly IReadOnlyList<string> _cols;
    private readonly IReadOnlyList<Record> _filas;
    private readonly int _porLote;

    public FakeSource(IReadOnlyList<string> cols, IReadOnlyList<Record> filas, int porLote)
    {
        _cols = cols; _filas = filas; _porLote = porLote;
    }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        for (int i = 0; i < _filas.Count; i += _porLote)
        {
            await Task.Yield();
            var slice = _filas.Skip(i).Take(_porLote).ToList();
            yield return new RecordBatch { Columns = _cols, Rows = slice };
        }
    }
}

/// <summary>Factory de prueba que devuelve siempre el mismo FakeSource.</summary>
internal sealed class FakeSourceFactory : ISourceFactory
{
    private readonly ISource _source;
    public FakeSourceFactory(ISource source) => _source = source;
    public ISource Create(StepDefinition step) => _source;
}
```

- [ ] **Step 2: Escribir el test que falla (PipelineRunner)**

`tests/DataPipeline.Tests/PipelineRunnerTests.cs`:
```csharp
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class PipelineRunnerTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"dpr_{Guid.NewGuid():N}.db")};Pooling=False";

    private static EncryptionService EncDummy() =>
        new(Convert.ToBase64String(new byte[32]));

    private static Record Fila(int id, string nombre) =>
        new() { Values = new Dictionary<string, object?> { ["id"] = id, ["nombre"] = nombre } };

    private static PipelineDefinition DefMinima() => new()
    {
        Id = Guid.NewGuid(),
        Nombre = "p",
        TablaDestino = "destino",
        ModoEscritura = ModoEscritura.Agregar,
        FilasPorLote = 2,
        Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
    };

    [Fact]
    public async Task Corre_source_a_store_y_reporta_filas()
    {
        var cs = NuevaDb();
        var store = new SqliteDataStore(cs);
        var source = new FakeSource(
            new[] { "id", "nombre" },
            new[] { Fila(1, "a"), Fila(2, "b"), Fila(3, "c") },
            porLote: 2);
        var runner = new PipelineRunner(new FakeSourceFactory(source), store);

        var result = await runner.RunAsync(DefMinima(), new Dictionary<string, string>(), EncDummy());

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.RowsRead);
        Assert.Equal(3, result.RowsWritten);

        var pagina = await store.ReadPagedAsync("destino", 1, 10, CancellationToken.None);
        Assert.Equal(3, pagina.Rows.Count);
    }

    [Fact]
    public async Task Falla_controlada_devuelve_Success_false_con_Error()
    {
        var store = new SqliteDataStore(NuevaDb());
        var def = DefMinima();
        def.Pasos.Add(new StepDefinition { Nombre = "t", Tipo = TipoPaso.Transformacion, Conector = "X" });
        var source = new FakeSource(new[] { "id" }, Array.Empty<Record>(), 5);
        var runner = new PipelineRunner(new FakeSourceFactory(source), store);

        var result = await runner.RunAsync(def, new Dictionary<string, string>(), EncDummy());

        Assert.False(result.Success);
        Assert.Contains("Transform", result.Error);
    }
}
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~PipelineRunnerTests"`
Expected: FAIL de compilación ("PipelineRunner/RunResult no existen").

- [ ] **Step 4: Implementar RunResult y PipelineRunner**

`DataPipeline.Engine/RunResult.cs`:
```csharp
namespace DataPipeline.Engine;

/// <summary>Resultado de una corrida de pipeline.</summary>
public sealed record RunResult(
    Guid RunId,
    bool Success,
    long RowsRead,
    long RowsWritten,
    string? Error,
    TimeSpan Duration);
```

`DataPipeline.Engine/PipelineRunner.cs`:
```csharp
using System.Diagnostics;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Arma y corre la tubería Source → IDataStore por lotes (Fase 1: sin transforms).</summary>
public sealed class PipelineRunner
{
    private readonly ISourceFactory _sourceFactory;
    private readonly IDataStore _dataStore;

    public PipelineRunner(ISourceFactory sourceFactory, IDataStore dataStore)
    {
        _sourceFactory = sourceFactory;
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
            if (def.Pasos.Any(s => s.Tipo == TipoPaso.Transformacion))
                throw new NotSupportedException("Las transformaciones (Transform) llegan en Fase 2.");

            var pasoSource = def.Pasos.SingleOrDefault(s => s.Tipo == TipoPaso.Origen)
                ?? throw new InvalidOperationException("El pipeline necesita exactamente un Origen.");

            var source = _sourceFactory.Create(pasoSource);
            var ctx = new RunContext
            {
                PipelineId = def.Id,
                RunId = runId,
                Parameters = parameters,
                Encryption = encryption,
                BatchSize = def.FilasPorLote
            };

            var tablaLista = false;
            await foreach (var lote in source.ReadAsync(ctx, ct).WithCancellation(ct))
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

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test --filter "FullyQualifiedName~PipelineRunnerTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Escribir el test que falla (PipelineJsonLoader)**

`tests/DataPipeline.Tests/PipelineJsonLoaderTests.cs`:
```csharp
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class PipelineJsonLoaderTests
{
    private const string Json = """
    {
      "nombre": "ventas_a_serving",
      "habilitado": true,
      "programacion": "Diaria",
      "horaDelDia": "02:30",
      "filasPorLote": 5000,
      "tablaDestino": "serving_ventas",
      "modoEscritura": "Reemplazar",
      "correrDespuesDe": [],
      "pasos": [
        {
          "nombre": "leer_ventas",
          "tipo": "Origen",
          "conector": "SqlServerSource",
          "configuracion": { "connectionString": "Server=x;", "sql": "SELECT 1 AS n" }
        }
      ]
    }
    """;

    [Fact]
    public void Parsea_los_campos_principales()
    {
        var def = PipelineJsonLoader.Load(Json);

        Assert.Equal("ventas_a_serving", def.Nombre);
        Assert.Equal(TipoProgramacion.Diaria, def.Programacion);
        Assert.Equal("serving_ventas", def.TablaDestino);
        Assert.Equal(ModoEscritura.Reemplazar, def.ModoEscritura);
        Assert.Equal(5000, def.FilasPorLote);
        Assert.Single(def.Pasos);
        Assert.Equal(TipoPaso.Origen, def.Pasos[0].Tipo);
        Assert.Equal("SqlServerSource", def.Pasos[0].Conector);
        Assert.Equal("SELECT 1 AS n", def.Pasos[0].Configuracion["sql"]);
    }

    [Fact]
    public void Genera_Id_cuando_no_viene_en_el_JSON()
    {
        var def = PipelineJsonLoader.Load(Json);
        Assert.NotEqual(Guid.Empty, def.Id);
    }
}
```

- [ ] **Step 7: Correr el test y verificar que falla**

Run: `dotnet test --filter "FullyQualifiedName~PipelineJsonLoaderTests"`
Expected: FAIL de compilación ("PipelineJsonLoader no existe").

- [ ] **Step 8: Implementar el loader**

`DataPipeline.Engine/PipelineJsonLoader.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Engine;

/// <summary>Carga una PipelineDefinition desde JSON (formato del apéndice A del spec).</summary>
public static class PipelineJsonLoader
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static PipelineDefinition Load(string json)
    {
        var def = JsonSerializer.Deserialize<PipelineDefinition>(json, _opts)
            ?? throw new InvalidOperationException("JSON de pipeline inválido (deserializó a null).");

        if (def.Id == Guid.Empty)
            def.Id = Guid.NewGuid();

        return def;
    }
}
```

- [ ] **Step 9: Correr todos los tests y verificar que pasan**

Run: `dotnet test`
Expected: PASS (todos: Encryption 2 + Sqlite 3 + RecordBatcher 2 + SourceFactory 3 + PipelineRunner 2 + JsonLoader 2 = 14).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(engine): PipelineRunner + PipelineJsonLoader con tests end-to-end"
```

---

### Task 8: CLI + verificación de aceptación contra SQL Server

**Files:**
- Modify: `DataPipeline.Cli/Program.cs`
- Create: `E:\Sistemas\MiddleWare\ejemplos\ventas_a_serving.json` (pipeline de ejemplo)

**Interfaces:**
- Consumes: `PipelineJsonLoader`, `PipelineRunner`, `SourceFactory`, `SqliteDataStore`, `EncryptionService` (Tasks 3–7).
- Produces: ejecutable `DataPipeline.Cli` que corre un pipeline desde JSON.

- [ ] **Step 1: Implementar la CLI**

`DataPipeline.Cli/Program.cs` (reemplazar todo el contenido):
```csharp
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Security;
using DataPipeline.Engine;

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: DataPipeline.Cli <pipeline.json> <sqlite.db> [claveBase64]");
    return 1;
}

var jsonPath = args[0];
var dbPath = args[1];
var claveBase64 = args.Length >= 3 ? args[2] : null;

var def = PipelineJsonLoader.Load(await File.ReadAllTextAsync(jsonPath));

var store = new SqliteDataStore($"Data Source={dbPath}");
var factory = new SourceFactory();
var runner = new PipelineRunner(factory, store);

// Si no se pasa clave, se usa una dummy válida (32 bytes) porque el ejemplo no cifra la conexión.
IEncryptionService enc = new EncryptionService(claveBase64 ?? Convert.ToBase64String(new byte[32]));

var result = await runner.RunAsync(def, new Dictionary<string, string>(), enc);

Console.WriteLine(
    $"Run {result.RunId} -> Success={result.Success} " +
    $"Read={result.RowsRead} Written={result.RowsWritten} " +
    $"Dur={result.Duration} Error={result.Error}");

return result.Success ? 0 : 2;
```

- [ ] **Step 2: Crear el pipeline de ejemplo**

`E:\Sistemas\MiddleWare\ejemplos\ventas_a_serving.json`:
```json
{
  "nombre": "ventas_a_serving",
  "habilitado": true,
  "programacion": "BajoDemanda",
  "filasPorLote": 5000,
  "tablaDestino": "serving_ventas",
  "modoEscritura": "Reemplazar",
  "correrDespuesDe": [],
  "pasos": [
    {
      "nombre": "leer_ventas",
      "tipo": "Origen",
      "conector": "SqlServerSource",
      "configuracion": {
        "connectionString": "Server=POSSERVER;Database=ventas;Trusted_Connection=True;TrustServerCertificate=True;",
        "sql": "SELECT TOP 1000 sucursal, fecha, monto, cantidad FROM ventas"
      }
    }
  ]
}
```
> Ajustar `connectionString` y el `SELECT` a una BD/tabla SQL Server reales del entorno. Para una prueba sin tabla de negocio, usar `"sql": "SELECT 1 AS n, 'ok' AS estado"`.

- [ ] **Step 3: Compilar la CLI**

Run: `dotnet build DataPipeline.Cli`
Expected: `Build succeeded`, 0 errores.

- [ ] **Step 4: Verificación de aceptación (manual, contra SQL Server real)**

Run (ajustar la connection string del JSON primero):
```bash
dotnet run --project DataPipeline.Cli -- ejemplos/ventas_a_serving.json salida.db
```
Expected: una línea `Run <guid> -> Success=True Read=N Written=N Dur=... Error=`.

Verificar que la tabla aterrizó en SQLite:
```bash
dotnet run --project DataPipeline.Cli -- ejemplos/ventas_a_serving.json salida.db
```
y luego inspeccionar `salida.db` con cualquier visor SQLite, o agregar temporalmente al final de `Program.cs` una llamada a `store.ReadPagedAsync("serving_ventas", 1, 5, default)` e imprimir las filas para confirmar.

**Criterio de éxito de la Fase 1:** la corrida imprime `Success=True` con `Read==Written>0` y la tabla `serving_ventas` existe en `salida.db` con esas filas — es decir, **un dato real salió de SQL Server por streaming, sin transformación, y aterrizó en SQLite, a partir de un pipeline definido en JSON a mano.**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(cli): harness de ejecución de pipelines desde JSON + ejemplo"
```

---

## Self-Review

**1. Spec coverage (Fase 1 del spec, sección 20):**
- "Crear solución `DataPipeline` (.slnx)" → Task 1 ✅
- "`Core`: PipelineDefinition, StepDefinition, Record, RecordBatch, ISource/ITransform/IDataStore, RunContext" → Task 2 ✅ (RetryPolicy se omite: no se usa hasta Fase 4 — YAGNI).
- "1 source SqlServer en streaming" → Task 5 ✅
- "1 sink: SqliteDataStore detrás de IDataStore" → Task 4 ✅
- "Extraer EncryptionService" → Task 3 ✅
- "PipelineRunner mínimo (source→store, sin transform)" → Task 7 ✅
- "Correr un pipeline definido en JSON" → Tasks 7 (loader) + 8 (CLI) ✅
- Criterio de éxito (SqlServer→SQLite por streaming vía JSON) → Task 8 Step 4 ✅

**2. Placeholder scan:** sin "TBD/TODO/etc."; todo el código está completo. Los únicos valores a sustituir son la connection string y el SQL del ejemplo (Task 8), explícitamente marcados como ajuste de entorno.

**3. Type consistency:** verificado contra el bloque Interfaces de cada tarea — `RecordBatch{Columns,Rows}`, `Record{Values}`, `IDataStore.EnsureTableAsync(table,columns,mode,ct)`/`WriteBatchAsync(table,batch,ct)`/`ReadPagedAsync(table,page,pageSize,ct)`, `ISource.ReadAsync(ctx,ct)`, `RunContext{PipelineId,RunId,Parameters,Encryption,BatchSize}`, `ISourceFactory.Create(step)`, `PipelineRunner(sourceFactory,dataStore).RunAsync(def,parameters,encryption,ct)`, `RunResult(RunId,Success,RowsRead,RowsWritten,Error,Duration)`, `PipelineJsonLoader.Load(json)` — nombres usados de forma idéntica en todas las tareas.

**4. Notas de alcance:** `ITransform` se define pero no se implementa (Fase 2); `ModoEscritura.Combinar` (upsert) lanza `NotSupportedException` (Fase 4); `CorrerDespuesDe`/`DataStoreSource`/scheduler/API PULL quedan fuera de Fase 1 por diseño.

**5. Convención de idioma (Opción 2):** los enums (`TipoPaso`/`TipoProgramacion`/`ModoEscritura` + valores) y las propiedades de `PipelineDefinition`/`StepDefinition` van en castellano; interfaces, tipos técnicos de runtime (`Record`/`RecordBatch`/`RunContext`/`RunResult`), servicios y métodos en inglés. Ver el "Mapa de nombres" en Global Constraints — es la referencia canónica.
