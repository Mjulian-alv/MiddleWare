# DataPipeline — Fase 3A (API de gestion + auth admin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exponer CRUD de pipelines por HTTP autenticado con rol `admin`, separado del usuario `pull` (solo-lectura de datos), sobre la API existente.

**Architecture:** Se extiende el `BasicAuthHandler` para resolver dos credenciales (`pull` y `admin`) y emitir un claim de rol. Se agrega `DeleteAsync` a `IPipelineStore`/`EfPipelineStore`. Un `PipelinesController` nuevo (gateado por rol `admin`) expone list/get/upsert/delete reusando `IPipelineStore`. Los controllers de Fase 2 (`data`/`run`/`runs`) quedan intactos (auth por esquema, sin rol), preservando sus tests.

**Tech Stack:** ASP.NET Core (Sdk.Web), Basic Auth handler propio, BCrypt.Net-Next, EF Core 10.0.8 (Sqlite), xUnit + Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.Sqlite (test).

**Spec:** `docs/superpowers/specs/2026-06-21-datapipeline-fase3-umbrella-design.md` (rebanada 3A).

## Global Constraints

- TargetFramework `net10.0`; `<Nullable>enable</Nullable>`; `<ImplicitUsings>enable</ImplicitUsings>`.
- `DataPipeline.Core` sigue SIN dependencias externas (solo BCL).
- **Convencion de idioma (Opcion 2):** props/enums de dominio en castellano; interfaces, tipos tecnicos de runtime, servicios, metodos y modismos .NET en ingles. Variables locales/comentarios en espanol. **Sin tildes ni `ñ` en identificadores** (si en comentarios/strings).
- Build pristine (0 warnings); **verificar con `dotnet build --no-incremental`** (el incremental enmascara CS warnings).
- En los proyectos de test con global `using Xunit`, alias `using Record = DataPipeline.Core.Records.Record;` solo si se usa `Record` (no aplica a este plan).
- Un commit como minimo por tarea. Commits directo a `main` (workflow del repo); incluir `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Verificar la suite completa con `dotnet test` (corre los 3 proyectos de test).

## Estado de partida (Fase 2, ya construido)

- `DataPipeline.Api/Auth/BasicAuthHandler.cs`: valida `PullAuth:Username` + `PullAuth:PasswordHash` (BCrypt); emite `ClaimsIdentity` con `ClaimTypes.Name`; `public new const string Scheme = "Basic"`.
- `DataPipeline.Api/Controllers/DataController.cs` y `RunsController.cs`: `[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]` (sin rol).
- `DataPipeline.Api/Program.cs`: DI completa; `AddAuthentication(Scheme).AddScheme<...>`; `AddAuthorization()`; `AddControllers()`; hosted scheduler.
- `DataPipeline.Core/Abstractions/IPipelineStore.cs`: `GetByIdAsync`, `GetByNombreAsync`, `GetAllAsync`, `UpsertAsync`, `ObtenerVencidosAsync`, `ActualizarProgramacionAsync` (sin Delete).
- `DataPipeline.Persistence/EfPipelineStore.cs`: implementa lo anterior sobre `MetadataDbContext`.
- `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`: helper `NuevoCtx()` (SQLite temporal + `EnsureCreated`), `Def(nombre, proxima, habilitado)`.
- `tests/DataPipeline.Api.Tests/ApiFactory.cs`: `WebApplicationFactory<Program>` que setea `ConnectionStrings:Metadata/Data`, `PullAuth:Username`="pull", `PullAuth:PasswordHash`=BCrypt("secreto123"), `Encryption:Key`. Helper `SeedPipelineConDatosAsync(nombre, tabla)`.
- Build pristine, 37 tests verdes.

---

## File Structure

```
DataPipeline.Core/
  Abstractions/IPipelineStore.cs        (MOD: + DeleteAsync)
DataPipeline.Persistence/
  EfPipelineStore.cs                    (MOD: + DeleteAsync)
DataPipeline.Api/
  Auth/BasicAuthHandler.cs              (MOD: dos credenciales + rol; constantes RolPull/RolAdmin)
  Controllers/PipelinesController.cs    (NEW: CRUD pipelines, rol admin)
  appsettings.json                      (MOD: + seccion AdminAuth)
tests/DataPipeline.Persistence.Tests/
  RepositoriosTests.cs                  (MOD: + test de DeleteAsync)
tests/DataPipeline.Api.Tests/
  ApiFactory.cs                         (MOD: + AdminAuth settings + helper CredAdmin/CredPull)
  GestionApiTests.cs                    (NEW: 401/403/200 + CRUD + 404)
```

---

### Task 1: `DeleteAsync` en `IPipelineStore` + `EfPipelineStore`

**Files:**
- Modify: `DataPipeline.Core/Abstractions/IPipelineStore.cs`
- Modify: `DataPipeline.Persistence/EfPipelineStore.cs`
- Test: `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`

**Interfaces:**
- Consumes: `IPipelineStore`, `EfPipelineStore`, `MetadataDbContext`, helpers `NuevoCtx()`/`Def(...)` (Fase 2).
- Produces: `Task<bool> IPipelineStore.DeleteAsync(Guid id, CancellationToken ct)` — borra y devuelve `true`; si no existe, `false` (sin lanzar). Implementado en `EfPipelineStore`.

- [ ] **Step 1: Escribir el test de DeleteAsync (al final de `RepositoriosTests`, antes del `}` de la clase)**

```csharp
    [Fact]
    public async Task Delete_borra_y_devuelve_true_o_false()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var def = Def("borrar", null);
        await store.UpsertAsync(def, CancellationToken.None);

        var borrado = await store.DeleteAsync(def.Id, CancellationToken.None);
        var leido = await store.GetByIdAsync(def.Id, CancellationToken.None);
        var inexistente = await store.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(borrado);
        Assert.Null(leido);
        Assert.False(inexistente);
    }
```

- [ ] **Step 2: Compilar para ver el RED**

Run: `dotnet build tests/DataPipeline.Persistence.Tests --nologo`
Expected: FALLA de compilacion (`IPipelineStore`/`EfPipelineStore` no tienen `DeleteAsync`).

- [ ] **Step 3: Agregar la firma a `IPipelineStore`**

En `DataPipeline.Core/Abstractions/IPipelineStore.cs`, agregar dentro de la interfaz (despues de `ActualizarProgramacionAsync`):
```csharp
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
```

- [ ] **Step 4: Implementar en `EfPipelineStore`**

En `DataPipeline.Persistence/EfPipelineStore.cs`, agregar al final de la clase (antes del `}` de cierre):
```csharp
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var def = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (def is null) return false;
        _db.Pipelines.Remove(def);
        await _db.SaveChangesAsync(ct);
        return true;
    }
```

- [ ] **Step 5: Correr el test y verificar verde**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: PASS (4 tests: los 3 de Fase 2 + el nuevo).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Core/Abstractions/IPipelineStore.cs DataPipeline.Persistence/EfPipelineStore.cs tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs
git commit -m "feat(persistence): IPipelineStore.DeleteAsync (borra por id, bool)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `BasicAuthHandler` — dos credenciales (`pull`/`admin`) + claim de rol

**Files:**
- Modify: `DataPipeline.Api/Auth/BasicAuthHandler.cs`
- Modify: `DataPipeline.Api/appsettings.json`
- Modify: `tests/DataPipeline.Api.Tests/ApiFactory.cs`

**Interfaces:**
- Consumes: `AuthenticationHandler<AuthenticationSchemeOptions>`, `IConfiguration`, `BCrypt.Net.BCrypt`, `ClaimTypes` (Fase 2).
- Produces:
  - `BasicAuthHandler.RolPull = "pull"`, `BasicAuthHandler.RolAdmin = "admin"` (consts).
  - El handler valida contra `PullAuth:*` (rol `pull`) y `AdminAuth:*` (rol `admin`); emite `ClaimTypes.Name` + `ClaimTypes.Role`. Credencial invalida en ambos -> `Fail`.
  - `ApiFactory` setea `AdminAuth:Username`="admin" + `AdminAuth:PasswordHash`=BCrypt("admin123"); expone `CredPull()`/`CredAdmin()`.

- [ ] **Step 1: Agregar settings de AdminAuth y helpers de credenciales en `ApiFactory`**

En `tests/DataPipeline.Api.Tests/ApiFactory.cs`, dentro de `ConfigureWebHost`, despues de la linea de `PullAuth:PasswordHash`, agregar:
```csharp
        builder.UseSetting("AdminAuth:Username", "admin");
        builder.UseSetting("AdminAuth:PasswordHash", BCrypt.Net.BCrypt.HashPassword("admin123"));
```
Y agregar estos dos metodos publicos estaticos a la clase `ApiFactory` (despues de `SeedPipelineConDatosAsync`):
```csharp
    public static System.Net.Http.Headers.AuthenticationHeaderValue CredPull() =>
        new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("pull:secreto123")));

    public static System.Net.Http.Headers.AuthenticationHeaderValue CredAdmin() =>
        new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("admin:admin123")));
```

- [ ] **Step 2: Escribir el test del handler (rol) — crear `GestionApiTests.cs` con solo los casos de auth por ahora**

`tests/DataPipeline.Api.Tests/GestionApiTests.cs`:
```csharp
using System.Net;
using Xunit;

namespace DataPipeline.Api.Tests;

public class GestionApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public GestionApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Sin_credenciales_devuelve_401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Pull_no_accede_a_gestion_403()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();
        var resp = await cliente.GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_accede_a_gestion_200()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        var resp = await cliente.GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
```

> Nota: estos tests dependen de `PipelinesController` (Task 3). En esta tarea (Task 2) van a fallar por ruta inexistente (404) aunque el rol funcione. Se dejan escritos; el RED de Task 2 se valida con el comportamiento del handler via los tests existentes de Pull (que deben seguir verdes) y el GREEN definitivo de auth llega al cerrar Task 3. Si se prefiere aislar, correr en Task 2 solo `PullApiTests`/`RunsApiTests` para confirmar que la refactor del handler no rompe Fase 2.

- [ ] **Step 3: Refactorizar `BasicAuthHandler` para dos credenciales + rol**

Reemplazar el cuerpo de `DataPipeline.Api/Auth/BasicAuthHandler.cs` por:
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
    // 'new' explicito: oculta intencionalmente la propiedad Scheme (AuthenticationScheme) de la base;
    // aca Scheme es el nombre del esquema ("Basic") que referencian controllers y Program.
    public new const string Scheme = "Basic";
    public const string RolPull = "pull";
    public const string RolAdmin = "admin";

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
                return Task.FromResult(AuthenticateResult.Fail("Esquema invalido."));

            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            if (partes.Length != 2) return Task.FromResult(AuthenticateResult.Fail("Credencial mal formada."));

            var rol = ResolverRol(partes[0], partes[1]);
            if (rol is null) return Task.FromResult(AuthenticateResult.Fail("Credenciales invalidas."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, partes[0]),
                new Claim(ClaimTypes.Role, rol)
            };
            var id = new ClaimsIdentity(claims, Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(id), Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Error de parseo."));
        }
    }

    /// <summary>Devuelve el rol si (usuario, clave) coincide con alguna credencial configurada; null si no.</summary>
    private string? ResolverRol(string usuario, string clave)
    {
        if (Coincide("PullAuth", usuario, clave)) return RolPull;
        if (Coincide("AdminAuth", usuario, clave)) return RolAdmin;
        return null;
    }

    private bool Coincide(string seccion, string usuario, string clave)
    {
        var u = _config[$"{seccion}:Username"];
        var h = _config[$"{seccion}:PasswordHash"];
        return u is not null && h is not null && usuario == u && BCrypt.Net.BCrypt.Verify(clave, h);
    }
}
```

- [ ] **Step 4: Agregar la seccion `AdminAuth` a `appsettings.json`**

En `DataPipeline.Api/appsettings.json`, despues del bloque `"PullAuth": { ... }`, agregar (recordar la coma entre objetos):
```json
  "AdminAuth": {
    "Username": "admin",
    "PasswordHash": "$2a$11$REEMPLAZAR_POR_HASH_BCRYPT"
  },
```

- [ ] **Step 5: Verificar que Fase 2 sigue verde (la refactor del handler no rompe nada)**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~PullApiTests|FullyQualifiedName~RunsApiTests" --nologo`
Expected: PASS (los 5 tests de Fase 2 de la API: `pull` sigue autenticando, ahora con rol `pull`).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Auth/BasicAuthHandler.cs DataPipeline.Api/appsettings.json tests/DataPipeline.Api.Tests/ApiFactory.cs tests/DataPipeline.Api.Tests/GestionApiTests.cs
git commit -m "feat(api): BasicAuth con dos credenciales (pull/admin) y claim de rol

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `PipelinesController` — CRUD de pipelines (rol admin)

**Files:**
- Create: `DataPipeline.Api/Controllers/PipelinesController.cs`
- Test: `tests/DataPipeline.Api.Tests/GestionApiTests.cs` (agregar casos CRUD)

**Interfaces:**
- Consumes: `IPipelineStore` (con `DeleteAsync` de Task 1), `PipelineDefinition`, `StepDefinition`, `TipoPaso`, `ModoEscritura`, `TipoProgramacion`, `BasicAuthHandler.Scheme`/`RolAdmin` (Task 2), `ApiFactory.CredAdmin()` (Task 2).
- Produces: rutas bajo `api/pipelines` gateadas por rol `admin`:
  - `GET /api/pipelines` -> `200` lista `PipelineDefinition[]`.
  - `GET /api/pipelines/{id:guid}` -> `200` def | `404`.
  - `POST /api/pipelines` (body `PipelineDefinition`) -> `200 { id }`; si `Id` viene `Guid.Empty`, asigna uno nuevo (create), si no, upsert (update).
  - `DELETE /api/pipelines/{id:guid}` -> `204` | `404`.

- [ ] **Step 1: Agregar los tests CRUD a `GestionApiTests.cs` (antes del `}` final de la clase)**

```csharp
    [Fact]
    public async Task Crud_completo_de_pipeline_como_admin()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();

        var def = new DataPipeline.Core.Pipelines.PipelineDefinition
        {
            Nombre = $"p_{Guid.NewGuid():N}",
            TablaDestino = "destino",
            ModoEscritura = DataPipeline.Core.Pipelines.ModoEscritura.Reemplazar,
            Programacion = DataPipeline.Core.Pipelines.TipoProgramacion.BajoDemanda,
            FilasPorLote = 100,
            Pasos =
            {
                new DataPipeline.Core.Pipelines.StepDefinition
                {
                    Nombre = "leer",
                    Tipo = DataPipeline.Core.Pipelines.TipoPaso.Origen,
                    Conector = "SqlServerSource"
                }
            }
        };

        // POST crea
        var post = await cliente.PostAsJsonAsync("/api/pipelines", def);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var creado = await post.Content.ReadFromJsonAsync<RespId>();
        Assert.NotNull(creado);
        Assert.NotEqual(Guid.Empty, creado!.Id);

        // GET by id devuelve
        var get = await cliente.GetAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // GET lista incluye al menos 1
        var lista = await cliente.GetFromJsonAsync<List<DataPipeline.Core.Pipelines.PipelineDefinition>>("/api/pipelines");
        Assert.NotNull(lista);
        Assert.Contains(lista!, p => p.Id == creado.Id);

        // DELETE borra (204) y luego GET da 404
        var del = await cliente.DeleteAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var get2 = await cliente.GetAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get2.StatusCode);
    }

    [Fact]
    public async Task Delete_inexistente_devuelve_404()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        var resp = await cliente.DeleteAsync($"/api/pipelines/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record RespId(Guid Id);
```

Y agregar al inicio del archivo (junto a los `using`):
```csharp
using System.Net.Http.Json;
using System.Collections.Generic;
```

- [ ] **Step 2: Correr y ver el RED**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~GestionApiTests" --nologo`
Expected: FALLA (no existe `PipelinesController`; rutas dan 404; `Admin_accede_a_gestion_200` y los CRUD fallan).

- [ ] **Step 3: Implementar `PipelinesController`**

`DataPipeline.Api/Controllers/PipelinesController.cs`:
```csharp
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/pipelines")]
public sealed class PipelinesController : ControllerBase
{
    private readonly IPipelineStore _store;
    public PipelinesController(IPipelineStore store) => _store = store;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _store.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var def = await _store.GetByIdAsync(id, ct);
        return def is null ? NotFound() : Ok(def);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PipelineDefinition def, CancellationToken ct)
    {
        if (def.Id == Guid.Empty) def.Id = Guid.NewGuid();
        await _store.UpsertAsync(def, ct);
        return Ok(new { def.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var borrado = await _store.DeleteAsync(id, ct);
        return borrado ? NoContent() : NotFound();
    }
}
```

- [ ] **Step 4: Correr los tests de gestion y verificar verde**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "FullyQualifiedName~GestionApiTests" --nologo`
Expected: PASS (5 tests: 401, 403, 200, CRUD completo, delete inexistente 404).

- [ ] **Step 5: Build pristine + suite completa**

Run: `dotnet build --no-incremental --nologo` -> 0 warnings.
Run: `dotnet test --nologo` -> PASS (todos los proyectos: Fase 2 + los nuevos de 3A).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Controllers/PipelinesController.cs tests/DataPipeline.Api.Tests/GestionApiTests.cs
git commit -m "feat(api): PipelinesController CRUD (rol admin) sobre IPipelineStore

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage (rebanada 3A):**
- "rol `admin` en el Basic Auth handler" -> Task 2 ✅
- "`GET /api/pipelines`, `GET /api/pipelines/{id}`, `POST /api/pipelines`, `DELETE /api/pipelines/{id}`" -> Task 3 ✅
- "Reusa `IPipelineStore`" -> Task 3 (+ `DeleteAsync` en Task 1) ✅
- Criterio "CRUD completo como admin; pull no accede (403)" -> tests de Task 2/3 (401/403/200 + CRUD) ✅

**2. Placeholder scan:** sin TBD/TODO. El unico valor a sustituir es `AdminAuth:PasswordHash` en `appsettings.json` (marcado; los tests setean el suyo en `ApiFactory`), simetrico al `PullAuth:PasswordHash` de Fase 2.

**3. Type consistency:** `IPipelineStore.DeleteAsync(Guid,CancellationToken)->Task<bool>` definido en Task 1 y consumido en Task 3; `BasicAuthHandler.Scheme`/`RolAdmin` definidos en Task 2 y usados en el `[Authorize]` de Task 3; `ApiFactory.CredPull()`/`CredAdmin()` definidos en Task 2 y usados en Tasks 2/3; `PipelineDefinition`/`StepDefinition`/enums de Fase 1 usados en el test de Task 3.

**4. Notas de alcance:**
- Los controllers de Fase 2 (`data`/`run`/`runs`) NO se gatean por rol: siguen con auth por esquema, y `pull` los sigue usando (sus 5 tests quedan verdes). Solo `pipelines` exige rol `admin`.
- `POST /api/pipelines` con enums: el test serializa `PipelineDefinition` con las opciones por defecto de System.Text.Json (enums como numero) y el controller deserializa con las mismas; round-trip consistente. No se agrega `JsonStringEnumConverter` en la API (fuera de alcance de 3A).
- Fuera de 3A: UI, DataSources, validacion de negocio del POST (formato de schedule, unicidad de Nombre la maneja el indice unico de EF y se cubre en 3E/validacion futura).
