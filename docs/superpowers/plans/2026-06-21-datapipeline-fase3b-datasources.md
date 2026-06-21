# DataPipeline — Fase 3B (DataSources: entidad + CRUD API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modelar DataSources como entidad propia reusable (almacen A), exponerlas por CRUD HTTP (rol admin) con secretos encriptados en reposo y nunca devueltos en GET, y permitir que un pipeline que referencia un `dataSourceId` corra **hidratando la config antes del `SourceFactory`** (Opcion A: sin tocar `ISourceFactory`/`PipelineRunner`).

**Architecture:** Entidad `DataSource` + `IDataSourceStore` en Core; `EfDataSourceStore` + `DbSet` + migracion en Persistence. Un `PipelineHydrator` (Engine) resuelve `dataSourceId` -> `connectionString` desencriptado e inyecta en `StepDefinition.Configuracion` antes de correr; los pasos con config inline pasan sin cambios. `DataSourcesController` (rol admin) hace CRUD (encripta al guardar, enmascara secretos al leer) + `POST /{id}/probar` (test de conexion via `IConnectionTester`). Las rutas de corrida (`RunsController`, `EjecutorProgramado`) llaman al hydrator antes de `RunAsync`.

**Tech Stack:** .NET 10, EF Core 10.0.8 (Sqlite), ASP.NET Core, Microsoft.Data.SqlClient (test de conexion), BCrypt (auth admin, ya), xUnit + Mvc.Testing.

**Spec:** `docs/superpowers/specs/2026-06-21-datapipeline-fase3-umbrella-design.md` (rebanada 3B). Decision del usuario: **Opcion A** + GET no devuelve secretos.

## Global Constraints

- TargetFramework `net10.0`; Nullable/ImplicitUsings enable.
- `DataPipeline.Core` SIN dependencias externas (solo BCL). EF/SqlClient nunca en Core.
- **Convencion de idioma (Opcion 2):** props/enums de dominio en castellano; interfaces/tipos runtime/servicios/metodos en ingles; locales/comentarios en espanol; **sin tildes ni `ñ` en identificadores**.
- Build pristine; verificar con `dotnet build --no-incremental`.
- Tests: alias `using Record = DataPipeline.Core.Records.Record;` solo si se usa `Record`.
- Un commit por tarea, directo a `main`, con `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Estado de partida (Fase 2 + 3A)

- Core: `PipelineDefinition`, `StepDefinition{Configuracion: Dictionary<string,JsonElement>}`, `TipoPaso`, `IEncryptionService{Encrypt,Decrypt}`, `IPipelineStore` (con `DeleteAsync`).
- Persistence: `MetadataDbContext{DbSet<PipelineDefinition> Pipelines; DbSet<PipelineRun> Corridas}` + `OnModelCreating`; `EfPipelineStore`; migracion `InitMetadata`.
- Connectors: `SqlServerSource(connectionString, sql, connectionStringEncrypted)`, `SourceFactory` (lee `connectionString`/`sql`/`connectionStringEncrypted` de `Configuracion`), `EncryptionService(claveBase64)`.
- Engine: `PipelineRunner(ISourceFactory, ITransformFactory, IDataStore)`, `EjecutorProgramado(IPipelineStore, IHistorialCorridas, PipelineRunner, IEncryptionService)`.
- Api: `BasicAuthHandler` (roles `pull`/`admin`), `RunsController(IPipelineStore, IHistorialCorridas, PipelineRunner, IEncryptionService)`, `PipelinesController` (rol admin), `Program.cs` con DI.
- Tests Api: `ApiFactory` con `CredPull()`/`CredAdmin()`, `SeedPipelineConDatosAsync`.
- 43 tests verdes, build pristine.

---

## File Structure

```
DataPipeline.Core/
  DataSources/DataSource.cs              (NEW: DataSource + TipoDataSource)
  Abstractions/IDataSourceStore.cs       (NEW)
  Abstractions/IConnectionTester.cs      (NEW)
DataPipeline.Persistence/
  MetadataDbContext.cs                   (MOD: + DbSet<DataSource> + config)
  EfDataSourceStore.cs                   (NEW)
  Migrations/*                           (NEW: AgregarDataSources)
DataPipeline.Connectors/
  Testing/ConnectionTester.cs            (NEW: abre SqlConnection, graceful)
DataPipeline.Engine/
  PipelineHydrator.cs                    (NEW: dataSourceId -> connectionString)
  EjecutorProgramado.cs                  (MOD: + PipelineHydrator, hidrata antes de RunAsync)
DataPipeline.Api/
  Controllers/DataSourcesController.cs   (NEW: CRUD + probar, rol admin)
  Controllers/RunsController.cs          (MOD: + PipelineHydrator antes de RunAsync)
  Program.cs                             (MOD: DI de IDataSourceStore, IConnectionTester, PipelineHydrator)
tests/DataPipeline.Persistence.Tests/
  DataSourceRepoTests.cs                 (NEW)
tests/DataPipeline.Tests/
  Fakes/FakeDataSourceStore.cs           (NEW)
  PipelineHydratorTests.cs               (NEW)
  EjecutorProgramadoTests.cs             (MOD: nuevo ctor)
tests/DataPipeline.Api.Tests/
  DataSourcesApiTests.cs                 (NEW)
```

---

### Task 1: `DataSource` (Core) + `EfDataSourceStore` + migracion (Persistence)

**Files:**
- Create: `DataPipeline.Core/DataSources/DataSource.cs`
- Create: `DataPipeline.Core/Abstractions/IDataSourceStore.cs`
- Modify: `DataPipeline.Persistence/MetadataDbContext.cs`
- Create: `DataPipeline.Persistence/EfDataSourceStore.cs`
- Test: `tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs`

**Interfaces:**
- Produces:
  - `enum TipoDataSource { SqlServer }`
  - `class DataSource { Guid Id; string Nombre; TipoDataSource Tipo; string? ConnectionString; string? Username; string? Password; string? BaseUrl; string? FilePath; bool Activo; DateTime CreatedAt; DateTime? UpdatedAt; }`
  - `interface IDataSourceStore { Task<DataSource?> GetByIdAsync(Guid,CancellationToken); Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken); Task UpsertAsync(DataSource,CancellationToken); Task<bool> DeleteAsync(Guid,CancellationToken); }`
  - `class EfDataSourceStore : IDataSourceStore`.

- [ ] **Step 1: Crear los tipos en Core**

`DataPipeline.Core/DataSources/DataSource.cs`:
```csharp
namespace DataPipeline.Core.DataSources;

/// <summary>Tipo de origen de datos. Fase 3 arranca con SqlServer; extensible.</summary>
public enum TipoDataSource { SqlServer }

/// <summary>Conexion nombrada reusable (almacen A). Secretos encriptados en reposo.</summary>
public sealed class DataSource
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public TipoDataSource Tipo { get; set; }
    public string? ConnectionString { get; set; } // encriptado
    public string? Username { get; set; }          // encriptado
    public string? Password { get; set; }          // encriptado
    public string? BaseUrl { get; set; }           // futuro (RestApi)
    public string? FilePath { get; set; }          // futuro (Excel)
    public bool Activo { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

`DataPipeline.Core/Abstractions/IDataSourceStore.cs`:
```csharp
using DataPipeline.Core.DataSources;

namespace DataPipeline.Core.Abstractions;

/// <summary>Almacen de DataSources (almacen A).</summary>
public interface IDataSourceStore
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(DataSource ds, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
```

- [ ] **Step 2: Escribir el test del repo**

`tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs`:
```csharp
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
}
```

- [ ] **Step 3: Compilar para ver el RED**

Run: `dotnet build tests/DataPipeline.Persistence.Tests --nologo`
Expected: FALLA (no existen `EfDataSourceStore` ni el `DbSet`).

- [ ] **Step 4: Agregar el `DbSet` y la config en `MetadataDbContext`**

En `DataPipeline.Persistence/MetadataDbContext.cs`, agregar el using arriba:
```csharp
using DataPipeline.Core.DataSources;
```
Agregar el DbSet (junto a los otros):
```csharp
    public DbSet<DataSource> Origenes => Set<DataSource>();
```
Y al final de `OnModelCreating` (antes del `}` de cierre del metodo), agregar:
```csharp
        var origen = b.Entity<DataSource>();
        origen.HasKey(d => d.Id);
        origen.HasIndex(d => d.Nombre).IsUnique();
        origen.Property(d => d.Tipo).HasConversion<string>();
```

- [ ] **Step 5: Implementar `EfDataSourceStore`**

`DataPipeline.Persistence/EfDataSourceStore.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfDataSourceStore : IDataSourceStore
{
    private readonly MetadataDbContext _db;
    public EfDataSourceStore(MetadataDbContext db) => _db = db;

    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Origenes.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
        await _db.Origenes.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(DataSource ds, CancellationToken ct)
    {
        var existe = await _db.Origenes.AnyAsync(d => d.Id == ds.Id, ct);
        if (existe) _db.Origenes.Update(ds);
        else _db.Origenes.Add(ds);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ds = await _db.Origenes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (ds is null) return false;
        _db.Origenes.Remove(ds);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 6: Correr el test del repo (verde; usa `EnsureCreated`)**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --filter "FullyQualifiedName~DataSourceRepoTests" --nologo`
Expected: PASS (2 tests).

- [ ] **Step 7: Generar la migracion**

Run: `dotnet ef migrations add AgregarDataSources --project DataPipeline.Persistence --startup-project DataPipeline.Persistence`
Verificar que se crean los 3 archivos en `DataPipeline.Persistence/Migrations/` (`*_AgregarDataSources.cs`, `.Designer.cs`, snapshot actualizado).

- [ ] **Step 8: Build pristine + commit**

Run: `dotnet build --no-incremental --nologo` -> 0 warnings.
```bash
git add DataPipeline.Core/DataSources/ DataPipeline.Core/Abstractions/IDataSourceStore.cs DataPipeline.Persistence/MetadataDbContext.cs DataPipeline.Persistence/EfDataSourceStore.cs DataPipeline.Persistence/Migrations/ tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs
git commit -m "feat(persistence): entidad DataSource + EfDataSourceStore + migracion (almacen A)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `PipelineHydrator` (Engine) — resuelve `dataSourceId` -> `connectionString`

**Files:**
- Create: `DataPipeline.Engine/PipelineHydrator.cs`
- Create: `tests/DataPipeline.Tests/Fakes/FakeDataSourceStore.cs`
- Test: `tests/DataPipeline.Tests/PipelineHydratorTests.cs`

**Interfaces:**
- Consumes: `IDataSourceStore` (Task 1), `IEncryptionService`, `PipelineDefinition`, `StepDefinition`, `TipoPaso`, `JsonElement`.
- Produces: `class PipelineHydrator(IDataSourceStore origenes, IEncryptionService encryption)` con `Task HydrateAsync(PipelineDefinition def, CancellationToken ct)`. Por cada paso Origen con `Configuracion["dataSourceId"]` (string guid): carga el DataSource, desencripta `ConnectionString` e inyecta `Configuracion["connectionString"]` (texto plano). Pasos sin `dataSourceId` quedan intactos (compat inline). DataSource inexistente: el paso queda sin cambios.

- [ ] **Step 1: Crear el fake store**

`tests/DataPipeline.Tests/Fakes/FakeDataSourceStore.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;

namespace DataPipeline.Tests.Fakes;

internal sealed class FakeDataSourceStore : IDataSourceStore
{
    private readonly Dictionary<Guid, DataSource> _datos = new();
    public FakeDataSourceStore(params DataSource[] ds) { foreach (var d in ds) _datos[d.Id] = d; }

    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_datos.TryGetValue(id, out var d) ? d : null);
    public Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult((IReadOnlyList<DataSource>)_datos.Values.ToList());
    public Task UpsertAsync(DataSource ds, CancellationToken ct) { _datos[ds.Id] = ds; return Task.CompletedTask; }
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(_datos.Remove(id));
}
```

- [ ] **Step 2: Escribir el test del hydrator**

`tests/DataPipeline.Tests/PipelineHydratorTests.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Connectors.Security;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;

namespace DataPipeline.Tests;

public class PipelineHydratorTests
{
    private static EncryptionService Enc() => new(Convert.ToBase64String(new byte[32]));
    private static JsonElement S(string v) => JsonSerializer.SerializeToElement(v);

    [Fact]
    public async Task Inyecta_connectionString_desencriptada_desde_el_datasource()
    {
        var enc = Enc();
        var id = Guid.NewGuid();
        var ds = new DataSource { Id = id, Nombre = "ventas", Tipo = TipoDataSource.SqlServer,
            ConnectionString = enc.Encrypt("Server=real;Database=db;") };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), enc);

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()), ["sql"] = S("SELECT 1") } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.True(cfg.ContainsKey("connectionString"));
        Assert.Equal("Server=real;Database=db;", cfg["connectionString"].GetString());
    }

    [Fact]
    public async Task Paso_con_config_inline_queda_intacto()
    {
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), Enc());
        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource",
                Configuracion = new() { ["connectionString"] = S("Server=inline;"), ["sql"] = S("SELECT 1") } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        Assert.Equal("Server=inline;", def.Pasos[0].Configuracion["connectionString"].GetString());
    }
}
```

- [ ] **Step 3: Correr y ver el RED**

Run: `dotnet test --filter "FullyQualifiedName~PipelineHydratorTests" --nologo`
Expected: FALLA (no existe `PipelineHydrator`).

- [ ] **Step 4: Implementar `PipelineHydrator`**

`DataPipeline.Engine/PipelineHydrator.cs`:
```csharp
using System.Text.Json;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>
/// Resuelve los pasos Origen que referencian un dataSourceId: carga el DataSource, desencripta su
/// connectionString e inyecta connectionString (texto plano) en la Configuracion del paso, ANTES de
/// que corra el SourceFactory. Pasos con config inline (sin dataSourceId) quedan intactos.
/// </summary>
public sealed class PipelineHydrator
{
    private readonly IDataSourceStore _origenes;
    private readonly IEncryptionService _encryption;

    public PipelineHydrator(IDataSourceStore origenes, IEncryptionService encryption)
    {
        _origenes = origenes;
        _encryption = encryption;
    }

    public async Task HydrateAsync(PipelineDefinition def, CancellationToken ct)
    {
        foreach (var paso in def.Pasos.Where(p => p.Tipo == TipoPaso.Origen))
        {
            if (!paso.Configuracion.TryGetValue("dataSourceId", out var idEl)
                || idEl.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idEl.GetString(), out var id))
                continue;

            var ds = await _origenes.GetByIdAsync(id, ct);
            if (ds is null) continue;

            var cs = string.IsNullOrEmpty(ds.ConnectionString) ? "" : _encryption.Decrypt(ds.ConnectionString);
            paso.Configuracion["connectionString"] = JsonSerializer.SerializeToElement(cs);
        }
    }
}
```

- [ ] **Step 5: Correr el test (verde)**

Run: `dotnet test --filter "FullyQualifiedName~PipelineHydratorTests" --nologo`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Engine/PipelineHydrator.cs tests/DataPipeline.Tests/Fakes/FakeDataSourceStore.cs tests/DataPipeline.Tests/PipelineHydratorTests.cs
git commit -m "feat(engine): PipelineHydrator resuelve dataSourceId -> connectionString (Opcion A)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `DataSourcesController` — CRUD (rol admin), encripta al guardar, enmascara al leer

**Files:**
- Create: `DataPipeline.Api/Controllers/DataSourcesController.cs`
- Modify: `DataPipeline.Api/Program.cs` (DI de `IDataSourceStore`)
- Test: `tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs`

**Interfaces:**
- Consumes: `IDataSourceStore`, `IEncryptionService`, `DataSource`, `TipoDataSource`, `BasicAuthHandler.Scheme/RolAdmin`, `ApiFactory.CredAdmin()/CredPull()`.
- Produces (todas bajo `api/datasources`, rol `admin`):
  - `GET` -> `200` lista de `DataSourceVista` (sin secretos).
  - `GET {id:guid}` -> `200 DataSourceVista` | `404`.
  - `POST` (body `DataSourceUpsert`, secretos en texto plano) -> `200 { id }`; encripta secretos no vacios; `Id` vacio -> nuevo.
  - `DELETE {id:guid}` -> `204` | `404`.
  - DTOs internos del controller: `record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo, DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString)` y `record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo, string? ConnectionString, string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo)`.

- [ ] **Step 1: Escribir el test (auth + CRUD + secretos ocultos)**

`tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs`:
```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataSourcesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DataSourcesApiTests(ApiFactory factory) => _factory = factory;

    private sealed record Upsert(Guid Id, string Nombre, int Tipo, string? ConnectionString,
        string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo);
    private sealed record Vista(Guid Id, string Nombre, int Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString);
    private sealed record RespId(Guid Id);

    [Fact]
    public async Task Pull_no_accede_403()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/datasources")).StatusCode);
    }

    [Fact]
    public async Task Crud_y_los_secretos_no_se_devuelven()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();

        var body = new Upsert(Guid.Empty, $"ds_{Guid.NewGuid():N}", 0,
            "Server=secreto;Database=db;", "user", "pass", null, null, true);

        var post = await c.PostAsJsonAsync("/api/datasources", body);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var creado = await post.Content.ReadFromJsonAsync<RespId>();
        Assert.NotEqual(Guid.Empty, creado!.Id);

        // GET by id: sin secretos, pero marca que tiene connectionString
        var vista = await c.GetFromJsonAsync<Vista>($"/api/datasources/{creado.Id}");
        Assert.NotNull(vista);
        Assert.True(vista!.TieneConnectionString);

        // El cuerpo crudo NO debe contener el secreto en claro
        var raw = await (await c.GetAsync($"/api/datasources/{creado.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Server=secreto", raw);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);

        // GET lista incluye al creado
        var lista = await c.GetFromJsonAsync<List<Vista>>("/api/datasources");
        Assert.Contains(lista!, v => v.Id == creado.Id);

        // DELETE 204 y luego 404
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/datasources/{creado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/datasources/{creado.Id}")).StatusCode);
    }
}
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataSourcesApiTests" --nologo`
Expected: FALLA (no existe el controller; rutas 404; `IDataSourceStore` no registrado).

- [ ] **Step 3: Registrar `IDataSourceStore` en `Program.cs`**

En `DataPipeline.Api/Program.cs`, junto a los otros `AddScoped` de repos (despues de `AddScoped<IHistorialCorridas, EfHistorialCorridas>();`), agregar:
```csharp
builder.Services.AddScoped<IDataSourceStore, EfDataSourceStore>();
```

- [ ] **Step 4: Implementar `DataSourcesController`**

`DataPipeline.Api/Controllers/DataSourcesController.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/datasources")]
public sealed class DataSourcesController : ControllerBase
{
    private readonly IDataSourceStore _store;
    private readonly IEncryptionService _enc;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc)
    {
        _store = store;
        _enc = enc;
    }

    public sealed record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString);

    public sealed record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo,
        string? ConnectionString, string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo);

    private static DataSourceVista AVista(DataSource d) =>
        new(d.Id, d.Nombre, d.Tipo, d.Activo, d.CreatedAt, d.UpdatedAt, !string.IsNullOrEmpty(d.ConnectionString));

    private string? Enc(string? plano) => string.IsNullOrEmpty(plano) ? null : _enc.Encrypt(plano);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok((await _store.GetAllAsync(ct)).Select(AVista));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        return d is null ? NotFound() : Ok(AVista(d));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] DataSourceUpsert req, CancellationToken ct)
    {
        var id = req.Id == Guid.Empty ? Guid.NewGuid() : req.Id;
        var existente = req.Id == Guid.Empty ? null : await _store.GetByIdAsync(id, ct);

        var ds = new DataSource
        {
            Id = id,
            Nombre = req.Nombre,
            Tipo = req.Tipo,
            ConnectionString = Enc(req.ConnectionString),
            Username = Enc(req.Username),
            Password = Enc(req.Password),
            BaseUrl = req.BaseUrl,
            FilePath = req.FilePath,
            Activo = req.Activo,
            CreatedAt = existente?.CreatedAt ?? DateTime.Now,
            UpdatedAt = existente is null ? null : DateTime.Now
        };
        await _store.UpsertAsync(ds, ct);
        return Ok(new { ds.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var borrado = await _store.DeleteAsync(id, ct);
        return borrado ? NoContent() : NotFound();
    }
}
```

> Nota de alcance 3B: en update, los secretos provistos pisan a los anteriores; si vienen vacios, se guardan null (la UX de "no re-enviar secreto para conservarlo" es de 3E). El indice unico de `Nombre` puede lanzar en POST con nombre repetido; el manejo amable de ese error queda para validacion futura.

- [ ] **Step 5: Correr el test (verde)**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataSourcesApiTests" --nologo`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Controllers/DataSourcesController.cs DataPipeline.Api/Program.cs tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs
git commit -m "feat(api): DataSourcesController CRUD (rol admin), encripta al guardar y oculta secretos

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Test de conexion — `IConnectionTester` + `ConnectionTester` + `POST /{id}/probar`

**Files:**
- Create: `DataPipeline.Core/Abstractions/IConnectionTester.cs`
- Create: `DataPipeline.Connectors/Testing/ConnectionTester.cs`
- Modify: `DataPipeline.Api/Controllers/DataSourcesController.cs` (endpoint `probar`)
- Modify: `DataPipeline.Api/Program.cs` (DI de `IConnectionTester`)
- Test: `tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs` (caso `probar` con fallo amable)

**Interfaces:**
- Produces:
  - `interface IConnectionTester { Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct); }` con `record ConnectionResult(bool Ok, string? Error)`.
  - `class ConnectionTester : IConnectionTester` (abre `SqlConnection`, captura excepcion -> `Ok=false`).
  - `POST /api/datasources/{id:guid}/probar` -> `200 { ok, error }` (desencripta el cs del datasource y prueba); `404` si el datasource no existe.

- [ ] **Step 1: Crear el contrato en Core**

`DataPipeline.Core/Abstractions/IConnectionTester.cs`:
```csharp
namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de probar una conexion a un origen.</summary>
public sealed record ConnectionResult(bool Ok, string? Error);

/// <summary>Prueba de conectividad contra un origen (no lee datos).</summary>
public interface IConnectionTester
{
    Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct);
}
```

- [ ] **Step 2: Escribir el test del endpoint `probar` (fallo amable)**

Agregar a `tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs` (antes del `}` final de la clase), y agregar el record `Probar` junto a los otros records internos:
```csharp
    private sealed record ProbarResp(bool Ok, string? Error);

    [Fact]
    public async Task Probar_un_origen_inalcanzable_devuelve_ok_false_sin_romper()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();

        var body = new Upsert(Guid.Empty, $"ds_{Guid.NewGuid():N}", 0,
            "Server=no.existe.invalid;Database=x;Connect Timeout=2;TrustServerCertificate=True;User Id=u;Password=p;",
            null, null, null, null, true);
        var creado = await (await c.PostAsJsonAsync("/api/datasources", body)).Content.ReadFromJsonAsync<RespId>();

        var resp = await c.PostAsync($"/api/datasources/{creado!.Id}/probar", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var r = await resp.Content.ReadFromJsonAsync<ProbarResp>();
        Assert.NotNull(r);
        Assert.False(r!.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public async Task Probar_origen_inexistente_404()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        var resp = await c.PostAsync($"/api/datasources/{Guid.NewGuid()}/probar", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
```

- [ ] **Step 3: Correr y ver el RED**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataSourcesApiTests" --nologo`
Expected: FALLA (no existe el endpoint `probar` ni `IConnectionTester`).

- [ ] **Step 4: Implementar `ConnectionTester` en Connectors**

`DataPipeline.Connectors/Testing/ConnectionTester.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>Abre una conexion al origen y la cierra; cualquier excepcion -> Ok=false con el mensaje.</summary>
public sealed class ConnectionTester : IConnectionTester
{
    public async Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var con = new SqlConnection(connectionString);
            await con.OpenAsync(ct);
            return new ConnectionResult(true, null);
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ex.Message);
        }
    }
}
```

- [ ] **Step 5: Registrar `IConnectionTester` y agregar el endpoint `probar`**

En `DataPipeline.Api/Program.cs`, agregar (junto a los otros `AddScoped`):
```csharp
builder.Services.AddScoped<IConnectionTester, DataPipeline.Connectors.Testing.ConnectionTester>();
```

En `DataPipeline.Api/Controllers/DataSourcesController.cs`:
- Agregar el campo y parametro de ctor para `IConnectionTester`. Reemplazar el ctor por:
```csharp
    private readonly IDataSourceStore _store;
    private readonly IEncryptionService _enc;
    private readonly IConnectionTester _tester;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc, IConnectionTester tester)
    {
        _store = store;
        _enc = enc;
        _tester = tester;
    }
```
- Agregar el endpoint (antes del `}` final de la clase):
```csharp
    [HttpPost("{id:guid}/probar")]
    public async Task<IActionResult> Probar(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();
        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _tester.ProbarSqlServerAsync(cs, ct);
        return Ok(new { ok = r.Ok, error = r.Error });
    }
```

- [ ] **Step 6: Correr los tests de datasources (verde)**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~DataSourcesApiTests" --nologo`
Expected: PASS (4 tests: 403, CRUD/secretos, probar fallo-amable, probar 404).

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Core/Abstractions/IConnectionTester.cs DataPipeline.Connectors/Testing/ConnectionTester.cs DataPipeline.Api/Controllers/DataSourcesController.cs DataPipeline.Api/Program.cs tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs
git commit -m "feat(api): POST /api/datasources/{id}/probar (test de conexion, fallo amable)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Cablear la hidratacion en las rutas de corrida

**Files:**
- Modify: `DataPipeline.Engine/EjecutorProgramado.cs`
- Modify: `DataPipeline.Api/Controllers/RunsController.cs`
- Modify: `DataPipeline.Api/Program.cs` (DI de `PipelineHydrator`)
- Modify: `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs` (nuevo ctor)

**Interfaces:**
- Consumes: `PipelineHydrator` (Task 2).
- Produces: `EjecutorProgramado` y `RunsController.Run` llaman `await hydrator.HydrateAsync(def, ct)` antes de `RunAsync`. `EjecutorProgramado` ctor suma `PipelineHydrator hydrator` (ultimo parametro).

- [ ] **Step 1: Ajustar el test de `EjecutorProgramado` al nuevo ctor (RED de compilacion)**

En `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`, agregar el using:
```csharp
using DataPipeline.Tests.Fakes;
```
(ya esta presente) y reemplazar la construccion del ejecutor:
```csharp
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc);
```
por:
```csharp
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        var ejecutor = new EjecutorProgramado(store, hist, runner, enc, hydrator);
```

- [ ] **Step 2: Compilar para ver el RED**

Run: `dotnet build tests/DataPipeline.Tests --nologo`
Expected: FALLA (el ctor de `EjecutorProgramado` aun toma 4 args).

- [ ] **Step 3: Modificar `EjecutorProgramado`**

En `DataPipeline.Engine/EjecutorProgramado.cs`:
- Agregar el campo y parametro:
```csharp
    private readonly PipelineHydrator _hydrator;
```
- Reemplazar el ctor por:
```csharp
    public EjecutorProgramado(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption, PipelineHydrator hydrator)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
        _hydrator = hydrator;
    }
```
- Dentro de `ProcesarVencidosAsync`, en el `try`, antes de `var result = await _runner.RunAsync(...)`, agregar:
```csharp
                await _hydrator.HydrateAsync(def, ct);
```

- [ ] **Step 4: Correr el test de `EjecutorProgramado` (verde)**

Run: `dotnet test --filter "FullyQualifiedName~EjecutorProgramadoTests" --nologo`
Expected: PASS (1 test; el pipeline usa conector "Fake" sin dataSourceId, la hidratacion es no-op).

- [ ] **Step 5: Cablear el hydrator en `RunsController` + DI**

En `DataPipeline.Api/Program.cs`, agregar:
```csharp
builder.Services.AddScoped<PipelineHydrator>();
```
En `DataPipeline.Api/Controllers/RunsController.cs`:
- Agregar el campo y parametro de ctor `PipelineHydrator hydrator` (ultimo), asignarlo a `_hydrator`.
- En `Run`, antes de `var result = await _runner.RunAsync(...)`, agregar:
```csharp
        await _hydrator.HydrateAsync(def, ct);
```

- [ ] **Step 6: Build pristine + suite completa**

Run: `dotnet build --no-incremental --nologo` -> 0 warnings.
Run: `dotnet test --nologo` -> PASS (todos los proyectos: Fase 2 + 3A + 3B).

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Engine/EjecutorProgramado.cs DataPipeline.Api/Controllers/RunsController.cs DataPipeline.Api/Program.cs tests/DataPipeline.Tests/EjecutorProgramadoTests.cs
git commit -m "feat(engine,api): hidratar dataSourceId antes de correr (RunsController + EjecutorProgramado)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage (rebanada 3B):**
- "entidad `DataSource` + `IDataSourceStore` + repo EF + migracion; secretos encriptados" -> Task 1 (entidad/repo/migracion) + Task 3 (encripta al guardar) ✅
- "`GET/POST/DELETE /api/datasources`" -> Task 3 ✅
- "`POST /api/datasources/{id}/probar`" -> Task 4 ✅
- "`SourceFactory` resuelve `DataSourceId`" via **Opcion A** -> Task 2 (hydrator) + Task 5 (cableado) ✅
- "un pipeline que referencia un `DataSourceId` corre" -> hydrator inyecta connectionString (Task 2, testeado); rutas de corrida hidratan (Task 5). e2e real contra SQL Server queda manual (sin DB en el entorno), consistente con Fase 1/2. ✅
- GET no devuelve secretos -> Task 3 (`DataSourceVista` sin secretos; test verifica que el cuerpo crudo no contiene el secreto) ✅

**2. Placeholder scan:** sin TBD/TODO. Notas de alcance marcadas (update pisa secretos; nombre unico).

**3. Type consistency:** `IDataSourceStore` (Task 1) consumido por `FakeDataSourceStore`/`PipelineHydrator` (Task 2), `DataSourcesController` (Task 3), `EfDataSourceStore` (Task 1). `PipelineHydrator(IDataSourceStore, IEncryptionService).HydrateAsync(def, ct)` definido en Task 2, usado en Tasks 5. `IConnectionTester.ProbarSqlServerAsync(string, ct)->ConnectionResult` definido en Task 4 y usado en el controller. `TipoDataSource.SqlServer = 0` (los tests POST mandan `Tipo: 0`). `EjecutorProgramado` nuevo ctor (5 args) en Task 5 alineado con su test.

**4. Notas de alcance:**
- Opcion A: NO se tocan `ISourceFactory` ni `PipelineRunner`. Solo `EjecutorProgramado` y `RunsController` suman el paso de hidratacion (additivo).
- El test de conexion abre `SqlConnection` real; en el entorno sin SQL Server se valida el camino de **fallo amable** (Ok=false). El camino Ok=true es verificable manualmente contra un SQL real.
- Enums por la API: el test manda `Tipo` como entero (`0` = SqlServer), consistente con System.Text.Json por defecto (sin `JsonStringEnumConverter`).
