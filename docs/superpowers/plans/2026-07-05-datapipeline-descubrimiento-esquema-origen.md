# Descubrimiento de esquema del origen ("traer columnas") — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el editor descubra las columnas del resultado del origen (corriendo el SELECT en modo solo-esquema) y las use para poblar y validar los selectores de watermark y columnas clave, persistiendo el esquema en el pipeline.

**Architecture:** `PipelineDefinition` suma `EsquemaOrigen` (List<ColumnaOrigen>). Un `ISchemaDiscoverer` (impl SQL Server con `CommandBehavior.SchemaOnly`) obtiene las columnas sin bajar datos; un endpoint `POST /api/datasources/{id}/columnas` lo expone. El editor lo llama con un boton "Traer columnas" y convierte watermark en dropdown y clave en multi-select. El runtime NO cambia: el serving se sigue creando desde el resultado real.

**Tech Stack:** .NET 10, C#, EF Core (SQLite), Microsoft.Data.SqlClient, ASP.NET Core, Blazor Server + Syncfusion, xUnit.

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches ni remoto. **NO usar `git add -A`**: stagear rutas explicitas.
- Gate de 0 warnings: verificar con `dotnet build --no-incremental`.
- Alcance = **ayuda de diseno**: el runtime (`PipelineRunner`/`SqliteDataStore`) NO se toca; el serving se sigue creando desde el resultado real.
- Nueva columna de metadata → propiedad EF + migracion (snapshot) + **linea `ALTER ADD COLUMN` en `MetadataSchemaInitializer` + columna en su `CREATE TABLE`** (el drift-guard test la exige).
- Watermark se elige de columnas del **origen** (pre-transform). Clave se elige de columnas del origen + validacion; calculo post-transform DIFERIDO (fuera de alcance).
- Descubrimiento: `ExecuteReader(CommandBehavior.SchemaOnly)`, bindeando `@watermark = DBNull` si el SQL lo referencia (misma guarda que `SqlServerSource`).
- Solo SQL Server (los demas motores, fuera de alcance).

---

### Task 1: Modelo `EsquemaOrigen` + persistencia + esquema

**Files:**
- Create: `DataPipeline.Core/Pipelines/ColumnaOrigen.cs`
- Modify: `DataPipeline.Core/Pipelines/PipelineDefinition.cs`
- Modify: `DataPipeline.Persistence/MetadataDbContext.cs`
- Modify: `DataPipeline.Persistence/MetadataSchemaInitializer.cs`
- Create: `DataPipeline.Persistence/Migrations/<timestamp>_AgregarEsquemaOrigen.cs` (via `dotnet ef`)
- Test: `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`, `tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs`

**Interfaces:**
- Produces: `DataPipeline.Core.Pipelines.ColumnaOrigen { string Nombre; string Tipo; }`; `PipelineDefinition.EsquemaOrigen` (`List<ColumnaOrigen>`).

- [ ] **Step 1: Crear el tipo `ColumnaOrigen`**

Crear `DataPipeline.Core/Pipelines/ColumnaOrigen.cs`:

```csharp
namespace DataPipeline.Core.Pipelines;

/// <summary>Una columna del resultado del origen, descubierta en diseno (nombre + tipo SQL).</summary>
public sealed class ColumnaOrigen
{
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = ""; // nombre del tipo del provider (ej. "datetime", "int", "nvarchar")
}
```

- [ ] **Step 2: Agregar `EsquemaOrigen` a `PipelineDefinition`**

En `PipelineDefinition.cs`, despues de `ColumnasClave` (linea 24) y antes de `ColumnaWatermark`:

```csharp
    /// <summary>Columnas del resultado del origen, descubiertas en diseno (ayuda para watermark/clave).</summary>
    public List<ColumnaOrigen> EsquemaOrigen { get; set; } = new();
```

- [ ] **Step 3: Mapear `EsquemaOrigen` como JSON TEXT en `MetadataDbContext`**

En `MetadataDbContext.cs`, despues del bloque de `ColumnasClave` (que termina en la linea con `pipeline.Property(p => p.ColumnasClave)...`), agregar el mismo patron converter+comparer:

```csharp
        var esquemaConverter = new ValueConverter<List<ColumnaOrigen>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<ColumnaOrigen>>(v, jsonOpts) ?? new());
        var esquemaComparer = new ValueComparer<List<ColumnaOrigen>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ColumnaOrigen>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.EsquemaOrigen).HasConversion(esquemaConverter).Metadata.SetValueComparer(esquemaComparer);
```

(El `using DataPipeline.Core.Pipelines;` ya esta en el archivo.)

- [ ] **Step 4: Generar la migracion EF**

Run:
```bash
dotnet ef migrations add AgregarEsquemaOrigen --project DataPipeline.Persistence --startup-project DataPipeline.Persistence
```
Verificar que el `Up` agrega la columna `EsquemaOrigen` TEXT NOT NULL DEFAULT '[]' (o defaultValue: "") sobre `Pipelines`.

- [ ] **Step 5: Agregar la columna al `MetadataSchemaInitializer`**

En `DataPipeline.Persistence/MetadataSchemaInitializer.cs`:

a) Dentro del `CREATE TABLE IF NOT EXISTS "Pipelines"`, agregar la columna (despues de `"ColumnasClave" ... DEFAULT '[]',`):

```sql
            "EsquemaOrigen"    TEXT    NOT NULL DEFAULT '[]',
```

b) En la seccion de ALTERs (junto a los de watermark), agregar:

```csharp
        """ALTER TABLE "Pipelines" ADD COLUMN "EsquemaOrigen" TEXT NOT NULL DEFAULT '[]'""",
```

- [ ] **Step 6: Escribir el test de persistencia (RED)**

En `RepositoriosTests.cs`, agregar (reusando el helper de contexto/store del archivo, `NuevoCtx()`/`Def()`):

```csharp
[Fact]
public async Task Pipeline_persiste_esquema_origen()
{
    await using var ctx = NuevoCtx();
    var store = new EfPipelineStore(ctx);
    var def = Def();
    def.EsquemaOrigen = new()
    {
        new ColumnaOrigen { Nombre = "id", Tipo = "int" },
        new ColumnaOrigen { Nombre = "fecha_mod", Tipo = "datetime" }
    };
    await store.UpsertAsync(def, CancellationToken.None);

    var leido = await store.GetByIdAsync(def.Id, CancellationToken.None);

    Assert.NotNull(leido);
    Assert.Equal(2, leido!.EsquemaOrigen.Count);
    Assert.Equal("fecha_mod", leido.EsquemaOrigen[1].Nombre);
    Assert.Equal("datetime", leido.EsquemaOrigen[1].Tipo);
}
```

> Abrir `RepositoriosTests.cs` primero y usar los helpers reales (`NuevoCtx()`, `Def()`); ajustar nombres si difieren. Asegurar el `using DataPipeline.Core.Pipelines;`.

- [ ] **Step 7: Actualizar el drift-guard del schema-init para ejercitar la columna**

En `tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs`, en el test `Crea_esquema_completo_y_permite_roundtrip_de_todas_las_entidades`, al construir el `PipelineDefinition`, agregar:

```csharp
                EsquemaOrigen = { new ColumnaOrigen { Nombre = "fecha", Tipo = "datetime" } },
```

(Asi el insert de EF ejercita la columna nueva; si faltara en el DDL del initializer, el test falla.)

- [ ] **Step 8: Correr los tests — RED luego GREEN**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: primero FAIL (columna inexistente / propiedad nueva), luego PASS tras los Steps 1-5.

- [ ] **Step 9: Commit**

```bash
git add DataPipeline.Core/Pipelines/ColumnaOrigen.cs DataPipeline.Core/Pipelines/PipelineDefinition.cs DataPipeline.Persistence/MetadataDbContext.cs DataPipeline.Persistence/MetadataSchemaInitializer.cs DataPipeline.Persistence/Migrations tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs
git commit -m "feat(core,persistence): PipelineDefinition.EsquemaOrigen + migracion + DDL idempotente"
```

---

### Task 2: `ISchemaDiscoverer` + impl SQL Server (build-only)

**Files:**
- Create: `DataPipeline.Core/Abstractions/ISchemaDiscoverer.cs`
- Create: `DataPipeline.Connectors/Testing/SqlServerSchemaDiscoverer.cs`

**Interfaces:**
- Consumes: `ColumnaOrigen` (Task 1).
- Produces: `SchemaResult(bool Ok, IReadOnlyList<ColumnaOrigen>? Columnas, string? Error)`; `ISchemaDiscoverer.DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct)`. Consumido por Task 3.

- [ ] **Step 1: Crear la abstraccion**

Crear `DataPipeline.Core/Abstractions/ISchemaDiscoverer.cs`:

```csharp
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de descubrir el esquema del resultado de un SQL (sin traer datos).</summary>
public sealed record SchemaResult(bool Ok, IReadOnlyList<ColumnaOrigen>? Columnas, string? Error);

/// <summary>Descubre las columnas del resultado de un SELECT sin ejecutar la consulta para datos.</summary>
public interface ISchemaDiscoverer
{
    Task<SchemaResult> DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct);
}
```

- [ ] **Step 2: Crear la impl SQL Server**

Crear `DataPipeline.Connectors/Testing/SqlServerSchemaDiscoverer.cs`:

```csharp
using System.Data;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>
/// Descubre columnas via CommandBehavior.SchemaOnly (no baja datos). Bindea @watermark=NULL si el SQL
/// lo referencia (misma guarda que SqlServerSource). Cualquier excepcion -> Ok=false con el mensaje.
/// </summary>
public sealed class SqlServerSchemaDiscoverer : ISchemaDiscoverer
{
    public async Task<SchemaResult> DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct)
    {
        try
        {
            await using var con = new SqlConnection(connectionString);
            await con.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 0 };
            if (sql.Contains("@watermark", StringComparison.OrdinalIgnoreCase))
                cmd.Parameters.AddWithValue("@watermark", DBNull.Value);

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
            var cols = new List<ColumnaOrigen>(rd.FieldCount);
            for (int i = 0; i < rd.FieldCount; i++)
                cols.Add(new ColumnaOrigen { Nombre = rd.GetName(i), Tipo = rd.GetDataTypeName(i) });

            return new SchemaResult(true, cols, null);
        }
        catch (Exception ex)
        {
            return new SchemaResult(false, null, ex.Message);
        }
    }
}
```

> Build-only: `SchemaOnly` requiere SQL Server real; se valida manual. No hay test automatizado de esta clase.

- [ ] **Step 3: Verificar build sin warnings**

Run: `dotnet build DataPipeline.Connectors --no-incremental --nologo`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add DataPipeline.Core/Abstractions/ISchemaDiscoverer.cs DataPipeline.Connectors/Testing/SqlServerSchemaDiscoverer.cs
git commit -m "feat(core,connectors): ISchemaDiscoverer + SqlServerSchemaDiscoverer (SchemaOnly)"
```

---

### Task 3: Endpoint `POST /api/datasources/{id}/columnas` + DI + test

**Files:**
- Modify: `DataPipeline.Api/Controllers/DataSourcesController.cs`
- Modify: `DataPipeline.Api/Program.cs`
- Test: `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs`

**Interfaces:**
- Consumes: `ISchemaDiscoverer`, `SchemaResult`, `ColumnaOrigen` (Tasks 1-2); `IDataSourceStore`, `IEncryptionService`, `IConnectionTester`.
- Produces: endpoint que devuelve `IReadOnlyList<ColumnaOrigen>` (200) / NotFound / BadRequest.

- [ ] **Step 1: Escribir el test del endpoint (RED)**

Crear `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs` — test unitario del controller con fakes (evita SQL Server):

```csharp
using DataPipeline.Api.Controllers;
using DataPipeline.Connectors.Security;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataSourcesColumnasTests
{
    private static EncryptionService Enc() => new(Convert.ToBase64String(new byte[32]));

    private sealed class FakeDsStore : IDataSourceStore
    {
        private readonly DataSource? _ds;
        public FakeDsStore(DataSource? ds) => _ds = ds;
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(_ds);
        public Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DataSource>>(Array.Empty<DataSource>());
        public Task UpsertAsync(DataSource ds, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class FakeTester : IConnectionTester
    {
        public Task<ConnectionResult> ProbarSqlServerAsync(string cs, CancellationToken ct) =>
            Task.FromResult(new ConnectionResult(true, null));
    }

    private sealed class FakeDiscoverer : ISchemaDiscoverer
    {
        private readonly SchemaResult _r;
        public FakeDiscoverer(SchemaResult r) => _r = r;
        public Task<SchemaResult> DescubrirSqlServerAsync(string cs, string sql, CancellationToken ct) =>
            Task.FromResult(_r);
    }

    private static DataSourcesController Controller(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString); // el controller desencripta
        return new DataSourcesController(new FakeDsStore(ds), enc, new FakeTester(), new FakeDiscoverer(r));
    }

    [Fact]
    public async Task Devuelve_las_columnas_descubiertas()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.SqlServer, ConnectionString = "Server=x;" };
        var cols = new List<ColumnaOrigen> { new() { Nombre = "id", Tipo = "int" } };
        var ctrl = Controller(ds, new SchemaResult(true, cols, null));

        var res = await ctrl.Columnas(ds.Id, new DataSourcesController.ColumnasReq("SELECT id FROM t"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var lista = Assert.IsAssignableFrom<IReadOnlyList<ColumnaOrigen>>(ok.Value);
        Assert.Single(lista);
        Assert.Equal("id", lista[0].Nombre);
    }

    [Fact]
    public async Task DataSource_inexistente_da_NotFound()
    {
        var ctrl = Controller(null, new SchemaResult(true, new List<ColumnaOrigen>(), null));
        var res = await ctrl.Columnas(Guid.NewGuid(), new DataSourcesController.ColumnasReq("SELECT 1"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(res);
    }

    [Fact]
    public async Task Error_del_discoverer_da_BadRequest()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.SqlServer, ConnectionString = "Server=x;" };
        var ctrl = Controller(ds, new SchemaResult(false, null, "sintaxis invalida"));
        var res = await ctrl.Columnas(ds.Id, new DataSourcesController.ColumnasReq("SELCT 1"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res);
    }
}
```

- [ ] **Step 2: Correr el test — debe fallar por compilacion**

Run: `dotnet test tests/DataPipeline.Api.Tests --nologo`
Expected: FAIL — `DataSourcesController.ColumnasReq`/`Columnas` y el 4to parametro del ctor no existen.

- [ ] **Step 3: Agregar el endpoint y el discoverer al controller**

En `DataSourcesController.cs`:

a) Agregar el campo y el parametro al ctor. Cambiar la firma y el cuerpo del constructor:

```csharp
    private readonly IDataSourceStore _store;
    private readonly IEncryptionService _enc;
    private readonly IConnectionTester _tester;
    private readonly ISchemaDiscoverer _discoverer;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc, IConnectionTester tester, ISchemaDiscoverer discoverer)
    {
        _store = store;
        _enc = enc;
        _tester = tester;
        _discoverer = discoverer;
    }
```

b) Agregar el record de request (junto a los otros records, despues de `DataSourceUpsert`):

```csharp
    public sealed record ColumnasReq(string Sql);
```

c) Agregar la accion (despues de `Probar`):

```csharp
    [HttpPost("{id:guid}/columnas")]
    public async Task<IActionResult> Columnas(Guid id, [FromBody] ColumnasReq req, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Sql)) return BadRequest(new { error = "SQL vacio." });

        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _discoverer.DescubrirSqlServerAsync(cs, req.Sql, ct);
        return r.Ok ? Ok(r.Columnas) : BadRequest(new { error = r.Error });
    }
```

(`using DataPipeline.Core.Pipelines;` no hace falta si solo se referencia via `SchemaResult`; agregarlo si el compilador lo pide.)

- [ ] **Step 4: Registrar `ISchemaDiscoverer` en DI**

En `DataPipeline.Api/Program.cs`, despues de la linea que registra `IConnectionTester` (linea 21):

```csharp
builder.Services.AddScoped<ISchemaDiscoverer, DataPipeline.Connectors.Testing.SqlServerSchemaDiscoverer>();
```

- [ ] **Step 5: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Api.Tests --nologo`
Expected: PASS (los 3 nuevos + los 17 previos).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Controllers/DataSourcesController.cs DataPipeline.Api/Program.cs tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs
git commit -m "feat(api): POST /api/datasources/{id}/columnas (descubre esquema del origen)"
```

---

### Task 4: Admin — servicio + editor (build-only)

**Files:**
- Modify: `DataPipeline.Admin/Services/DataSourcesAdminService.cs`
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor`

**Interfaces:**
- Consumes: endpoint `/columnas` (Task 3); `PipelineDefinition.EsquemaOrigen` (Task 1).

> Build-only: la UI Blazor/Syncfusion se prueba manual.

- [ ] **Step 1: Agregar `TraerColumnasAsync` al servicio**

En `DataSourcesAdminService.cs`, agregar el DTO (despues de `ProbarResp`) y el metodo (dentro de la clase):

```csharp
/// <summary>Una columna del resultado del origen (nombre + tipo).</summary>
public sealed record ColumnaOrigenDto(string Nombre, string Tipo);
```

```csharp
    public async Task<List<ColumnaOrigenDto>?> TraerColumnasAsync(Guid id, string sql)
    {
        var r = await _http.PostAsJsonAsync($"api/datasources/{id}/columnas", new { sql });
        if (!r.IsSuccessStatusCode) return null; // null = error (SQL invalido, conexion, etc.)
        return JsonSerializer.Deserialize<List<ColumnaOrigenDto>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }
```

- [ ] **Step 2: Cambiar el form del editor para clave-lista y esquema**

En `Editor.razor`, en `PipelineForm`, reemplazar `public string ColumnasClave { get; set; } = "";` por una lista, y agregar el esquema descubierto:

```csharp
        public List<string> ColumnasClave { get; set; } = new();
        public List<DataSourcesAdminService.ColumnaOrigenDto> EsquemaOrigen { get; set; } = new();
```

(El `@using DataPipeline.Admin.Services` ya deberia estar; si no, agregarlo arriba del `.razor`.)

- [ ] **Step 3: Boton "Traer columnas" + estado en la seccion Origen**

En la card "Origen" (despues del `<SfTextBox ... @bind-Value="_form.Sql" />`, linea 111), agregar el boton y un aviso:

```razor
            <div class="mt-2 d-flex align-items-center gap-2">
                <SfButton CssClass="e-small" OnClick="TraerColumnas" Disabled="@(_form.DataSourceId is null || string.IsNullOrWhiteSpace(_form.Sql))">
                    Traer columnas
                </SfButton>
                @if (_columnasMsg is not null)
                {
                    <span class="small text-muted">@_columnasMsg</span>
                }
            </div>
```

En `@code`, agregar el estado y el handler:

```csharp
    private string? _columnasMsg;

    private async Task TraerColumnas()
    {
        _columnasMsg = "Trayendo...";
        var cols = await Origenes.TraerColumnasAsync(_form.DataSourceId!.Value, _form.Sql);
        if (cols is null)
        {
            _columnasMsg = "No se pudieron traer las columnas (revisa el SQL o la conexion).";
            return;
        }
        _form.EsquemaOrigen = cols;
        _columnasMsg = $"{cols.Count} columnas.";
    }

    private List<string> NombresColumnas => _form.EsquemaOrigen.Select(c => c.Nombre).ToList();
```

- [ ] **Step 4: Watermark como dropdown, clave como multi-select**

Reemplazar el input de "Columnas clave" (el `<div class="mb-3">` con el `SfTextBox` de `_form.ColumnasClave`, lineas 35-37) por un multi-select:

```razor
            <div class="mb-3">
                <SfMultiSelect TValue="List<string>" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnasClave"
                               Placeholder="Columnas clave (requerido si Reemplazar)" FloatLabelType="FloatLabelType.Always" AllowCustomValue="true" />
            </div>
```

Reemplazar el input de "Columna watermark" (el `SfTextBox` agregado en 4C, con `@bind-Value="_form.ColumnaWatermark"`) por un dropdown:

```razor
            <div class="mb-3">
                <SfDropDownList TValue="string" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnaWatermark"
                                Placeholder="Columna watermark (opcional)" FloatLabelType="FloatLabelType.Always" AllowFiltering="true" />
                <div class="form-text">
                    Tu SQL debe filtrar con @@watermark (ej. WHERE @@watermark IS NULL OR fecha_mod &gt; @@watermark).
                    Si la columna es fecha, debe ser datetime/datetime2. Usa "Traer columnas" para elegir de la lista.
                </div>
            </div>
```

(`AllowCustomValue`/`AllowFiltering` permiten conservar un valor guardado que no este en la lista descubierta — pipelines viejos.)

- [ ] **Step 5: Ajustar `Guardar`, `ConstruirDef` y `Cargar` a la clave-lista + esquema**

En `Guardar()`, la validacion de Reemplazar (linea ~241) pasa de string a lista:

```csharp
        if (_form.ModoEscritura == 2 && _form.ColumnasClave.Count == 0)
        {
            _error = "Reemplazar requiere al menos una columna clave.";
            return;
        }
```

En `ConstruirDef()`, reemplazar el mapeo de `ColumnasClave` (el `.Split(...)`) por la lista directa, y sumar `EsquemaOrigen`:

```csharp
            ColumnasClave = _form.ColumnasClave.Select(c => c.Trim()).Where(c => c.Length > 0).ToList(),
            EsquemaOrigen = _form.EsquemaOrigen
                .Select(c => new ColumnaOrigen { Nombre = c.Nombre, Tipo = c.Tipo })
                .ToList(),
```

(Agregar `using DataPipeline.Core.Pipelines;` al `.razor` si no esta, para `ColumnaOrigen`.)

En `Cargar(def)`, reemplazar `_form.ColumnasClave = string.Join(", ", def.ColumnasClave);` por:

```csharp
        _form.ColumnasClave = def.ColumnasClave.ToList();
        _form.EsquemaOrigen = def.EsquemaOrigen
            .Select(c => new DataSourcesAdminService.ColumnaOrigenDto(c.Nombre, c.Tipo))
            .ToList();
```

- [ ] **Step 6: Verificar build sin warnings**

Run: `dotnet build DataPipeline.Admin --no-incremental --nologo`
Expected: Build succeeded, 0 warnings. (Prestar atencion a errores de binding de Syncfusion `SfMultiSelect`/`SfDropDownList` con `TValue`.)

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Admin/Services/DataSourcesAdminService.cs DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "feat(admin): boton Traer columnas + watermark/clave como selectores [build-only]"
```

---

### Task 5: Verificacion final

**Files:** (ninguno — solo verificacion)

- [ ] **Step 1: Build completo sin warnings**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Suite completa verde**

Run: `dotnet test --nologo`
Expected: todos verdes (los previos + los nuevos de Tasks 1 y 3).

- [ ] **Step 3: Confirmar historia limpia**

Run: `git log --oneline -6`
Expected: los commits de Tasks 1-4 en `main`, sin `git add -A`.

---

## Notas para quien ejecute

- **Ayuda de diseno, runtime intacto:** `EsquemaOrigen` es metadata; el `PipelineRunner`/`SqliteDataStore` NO se tocan. El serving se sigue creando desde el resultado real.
- **Verificacion manual (build-only):** contra un SQL Server real, en el editor: elegir DataSource + SQL, click "Traer columnas", confirmar que watermark (dropdown) y clave (multi-select) se pueblan; guardar y reabrir para confirmar que persiste (`EsquemaOrigen`) y valida.
- **Diferido:** calcular nombres post-transform para la clave; esquema autoritativo sobre el serving; discoverers para otros motores; auto-refresh al editar el SQL. Y la renovacion de la UI del Admin (tarea aparte).
