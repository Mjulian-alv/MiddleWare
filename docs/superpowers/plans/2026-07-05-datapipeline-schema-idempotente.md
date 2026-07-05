# Esquema idempotente al arranque (metadata) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar `EnsureCreated()` por un inicializador de esquema idempotente (DDL `CREATE TABLE IF NOT EXISTS` + `ALTER TABLE ADD COLUMN`) que pone al dia `metadata.db` en cada arranque sin borrar datos, auto-curando bases viejas.

**Architecture:** Un `MetadataSchemaInitializer` estatico en `DataPipeline.Persistence` corre una lista ordenada de sentencias DDL sobre la `DbConnection` del `MetadataDbContext`, tragando errores "duplicate column"/"already exists". La API y el CLI lo llaman en vez de `EnsureCreated`. El serving (`data.db`) y las migraciones EF no cambian.

**Tech Stack:** .NET 10, C#, EF Core (SQLite), Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches ni remoto. **NO usar `git add -A`**: stagear rutas explicitas.
- Gate de 0 warnings: verificar con `dotnet build --no-incremental`.
- Alcance: SOLO `metadata.db` (tablas `Origenes`, `Pipelines`, `Corridas`). El serving (`data.db`/`SqliteDataStore`) NO se toca.
- Las migraciones EF se conservan pero YA NO se aplican en runtime; el inicializador DDL es la fuente de verdad del esquema en runtime.
- Cada sentencia DDL en su propio try/catch: tragar `"duplicate column"`/`"already exists"` (OrdinalIgnoreCase); cualquier otro error → `logger?.LogWarning` (NO tumbar el arranque).
- Los fixtures de tests existentes siguen usando `EnsureCreated()` — no se tocan.

---

### Task 1: `MetadataSchemaInitializer` + tests

**Files:**
- Create: `DataPipeline.Persistence/MetadataSchemaInitializer.cs`
- Test: `tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs`

**Interfaces:**
- Produces: `public static Task MetadataSchemaInitializer.EnsureAsync(System.Data.Common.DbConnection connection, Microsoft.Extensions.Logging.ILogger? logger, CancellationToken ct = default)` — abre la conexion si esta cerrada y ejecuta el DDL idempotente. Consumido por Task 2 (API/CLI).

- [ ] **Step 1: Escribir los tests (fallan por no existir el tipo)**

Crear `tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs`:

```csharp
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class MetadataSchemaInitializerTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"schemainit_{Guid.NewGuid():N}.db")};Pooling=False";

    private static MetadataDbContext Ctx(string cs) =>
        new(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);

    private static async Task<HashSet<string>> ColumnasDe(SqliteConnection cn, string tabla)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{tabla}')";
        var cols = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) cols.Add(rd.GetString(1)); // col 1 = "name"
        return cols;
    }

    private static async Task Ejecutar(SqliteConnection cn, string sql)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Crea_esquema_completo_y_permite_roundtrip_de_todas_las_entidades()
    {
        var cs = NuevaDb();
        await using (var cn = new SqliteConnection(cs))
        {
            await cn.OpenAsync();
            await MetadataSchemaInitializer.EnsureAsync(cn, null);
        }

        // EF sobre la MISMA base: si el DDL olvido una columna, estos inserts fallan (drift guard).
        var pid = Guid.NewGuid();
        await using (var ctx = Ctx(cs))
        {
            ctx.Pipelines.Add(new PipelineDefinition
            {
                Id = pid, Nombre = "p1", Habilitado = true,
                Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
                DiaDeLaSemana = 1, DiaDelMes = 1,
                ProximaCorrida = DateTime.UtcNow, UltimaCorrida = DateTime.UtcNow,
                TablaDestino = "t", ModoEscritura = ModoEscritura.Reemplazar,
                ColumnasClave = { "id" }, CorrerDespuesDe = { Guid.NewGuid() },
                ColumnaWatermark = "fecha", UltimoWatermark = "2026-01-01T00:00:00.0000000",
                FilasPorLote = 10,
                Pasos = { new StepDefinition { Nombre = "o", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
            });
            ctx.Origenes.Add(new DataSource
            {
                Id = Guid.NewGuid(), Nombre = "ds1", Tipo = TipoDataSource.SqlServer,
                ConnectionString = "x", Username = "u", Password = "p",
                BaseUrl = "b", FilePath = "f", Activo = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            ctx.Corridas.Add(new PipelineRun
            {
                Id = Guid.NewGuid(), PipelineId = pid, Inicio = DateTime.UtcNow, Fin = DateTime.UtcNow,
                Estado = EstadoCorrida.Ok, FilasLeidas = 1, FilasEscritas = 1, FilasError = 0, Mensaje = "ok"
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = Ctx(cs);
        Assert.NotNull(await ctx2.Pipelines.FindAsync(pid));
        Assert.Equal(1, await ctx2.Origenes.CountAsync());
        Assert.Equal(1, await ctx2.Corridas.CountAsync());
    }

    [Fact]
    public async Task Es_idempotente_al_correr_dos_veces()
    {
        var cs = NuevaDb();
        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();

        await MetadataSchemaInitializer.EnsureAsync(cn, null);
        await MetadataSchemaInitializer.EnsureAsync(cn, null); // no debe tirar

        var cols = await ColumnasDe(cn, "Pipelines");
        Assert.Contains("ColumnaWatermark", cols);
        Assert.Contains("UltimoWatermark", cols);
    }

    [Fact]
    public async Task Agrega_columnas_faltantes_sobre_una_base_vieja()
    {
        var cs = NuevaDb();
        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();

        // Simular una base pre-4C: Pipelines con el esquema anterior (con ColumnasClave, SIN las 2 de watermark).
        await Ejecutar(cn, """
            CREATE TABLE "Pipelines" (
                "Id" TEXT NOT NULL PRIMARY KEY, "Nombre" TEXT NOT NULL, "Habilitado" INTEGER NOT NULL,
                "Programacion" TEXT NOT NULL, "HoraDelDia" TEXT NULL, "DiaDeLaSemana" INTEGER NULL,
                "DiaDelMes" INTEGER NULL, "ProximaCorrida" TEXT NULL, "UltimaCorrida" TEXT NULL,
                "Pasos" TEXT NOT NULL, "TablaDestino" TEXT NOT NULL, "ModoEscritura" TEXT NOT NULL,
                "CorrerDespuesDe" TEXT NOT NULL, "ColumnasClave" TEXT NOT NULL DEFAULT '[]', "FilasPorLote" INTEGER NOT NULL
            )
            """);

        await MetadataSchemaInitializer.EnsureAsync(cn, null);

        var cols = await ColumnasDe(cn, "Pipelines");
        Assert.Contains("ColumnaWatermark", cols);
        Assert.Contains("UltimoWatermark", cols);
    }
}
```

- [ ] **Step 2: Correr los tests — deben fallar por compilacion**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: FAIL — `MetadataSchemaInitializer` no existe (error de compilacion).

- [ ] **Step 3: Crear `MetadataSchemaInitializer`**

Crear `DataPipeline.Persistence/MetadataSchemaInitializer.cs`:

```csharp
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace DataPipeline.Persistence;

/// <summary>
/// Pone al dia el esquema del almacen A (metadata) de forma idempotente y segura sobre bases
/// existentes. Reemplaza EnsureCreated: corre DDL (CREATE TABLE IF NOT EXISTS + ALTER ADD COLUMN)
/// tragando "duplicate column"/"already exists". Es la fuente de verdad del esquema en runtime;
/// al cambiar el modelo EF hay que agregar aca la linea ALTER ADD COLUMN correspondiente.
/// </summary>
public static class MetadataSchemaInitializer
{
    private static readonly string[] Sentencias =
    {
        """
        CREATE TABLE IF NOT EXISTS "Origenes" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Origenes" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Tipo"             TEXT    NOT NULL,
            "ConnectionString" TEXT    NULL,
            "Username"         TEXT    NULL,
            "Password"         TEXT    NULL,
            "BaseUrl"          TEXT    NULL,
            "FilePath"         TEXT    NULL,
            "Activo"           INTEGER NOT NULL,
            "CreatedAt"        TEXT    NOT NULL,
            "UpdatedAt"        TEXT    NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Origenes_Nombre" ON "Origenes" ("Nombre")""",
        """
        CREATE TABLE IF NOT EXISTS "Pipelines" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Pipelines" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Habilitado"       INTEGER NOT NULL,
            "Programacion"     TEXT    NOT NULL,
            "HoraDelDia"       TEXT    NULL,
            "DiaDeLaSemana"    INTEGER NULL,
            "DiaDelMes"        INTEGER NULL,
            "ProximaCorrida"   TEXT    NULL,
            "UltimaCorrida"    TEXT    NULL,
            "Pasos"            TEXT    NOT NULL,
            "TablaDestino"     TEXT    NOT NULL,
            "ModoEscritura"    TEXT    NOT NULL,
            "CorrerDespuesDe"  TEXT    NOT NULL,
            "ColumnasClave"    TEXT    NOT NULL DEFAULT '[]',
            "ColumnaWatermark" TEXT    NULL,
            "UltimoWatermark"  TEXT    NULL,
            "FilasPorLote"     INTEGER NOT NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Pipelines_Nombre"         ON "Pipelines" ("Nombre")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Pipelines_ProximaCorrida" ON "Pipelines" ("ProximaCorrida")""",
        """
        CREATE TABLE IF NOT EXISTS "Corridas" (
            "Id"            TEXT    NOT NULL CONSTRAINT "PK_Corridas" PRIMARY KEY,
            "PipelineId"    TEXT    NOT NULL,
            "Inicio"        TEXT    NOT NULL,
            "Fin"           TEXT    NULL,
            "Estado"        TEXT    NOT NULL,
            "FilasLeidas"   INTEGER NOT NULL,
            "FilasEscritas" INTEGER NOT NULL,
            "FilasError"    INTEGER NOT NULL,
            "Mensaje"       TEXT    NULL
        )
        """,
        """CREATE INDEX IF NOT EXISTS "IX_Corridas_Inicio" ON "Corridas" ("Inicio")""",
        // Columnas agregadas despues del esquema inicial (auto-cura de bases viejas).
        // Sobre base nueva fallan con "duplicate column" (tragado). Requeridas llevan DEFAULT.
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnasClave"    TEXT NOT NULL DEFAULT '[]'""",
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnaWatermark" TEXT NULL""",
        """ALTER TABLE "Pipelines" ADD COLUMN "UltimoWatermark"  TEXT NULL""",
    };

    public static async Task EnsureAsync(DbConnection connection, ILogger? logger, CancellationToken ct = default)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        foreach (var sql in Sentencias)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex) when (
                ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // La tabla/columna/indice ya existe — caso normal sobre una base existente.
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Metadata schema: {Msg}", ex.Message);
            }
        }
    }
}
```

> Nota: `ILogger` vive en `Microsoft.Extensions.Logging.Abstractions`, dependencia transitiva de EF Core, asi que `DataPipeline.Persistence` ya la tiene. Si el tipo no resolviera, agregar el paquete: `dotnet add DataPipeline.Persistence package Microsoft.Extensions.Logging.Abstractions`.

- [ ] **Step 4: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: PASS (los 3 nuevos + los 9 previos).

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Persistence/MetadataSchemaInitializer.cs tests/DataPipeline.Persistence.Tests/MetadataSchemaInitializerTests.cs
git commit -m "feat(persistence): MetadataSchemaInitializer (DDL idempotente para metadata)"
```

---

### Task 2: Usar el inicializador en API y CLI (reemplaza `EnsureCreated`)

**Files:**
- Modify: `DataPipeline.Api/Program.cs:40-42`
- Modify: `DataPipeline.Cli/Program.cs:23` y `DataPipeline.Cli/Program.cs:69`

**Interfaces:**
- Consumes: `MetadataSchemaInitializer.EnsureAsync(DbConnection, ILogger?, CancellationToken)` (Task 1); `DbContext.Database.GetDbConnection()` (EF Core).

- [ ] **Step 1: Reemplazar `EnsureCreated` en la API**

En `DataPipeline.Api/Program.cs`, agregar el using de logging arriba (junto a los otros `using`, despues de `using Microsoft.EntityFrameworkCore;`):

```csharp
using Microsoft.Extensions.Logging;
```

Reemplazar el bloque actual (lineas 40-42):

```csharp
// Crear el esquema del almacen A al arrancar.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<MetadataDbContext>().Database.EnsureCreated();
```

por:

```csharp
// Poner al dia el esquema del almacen A al arrancar (idempotente, seguro sobre bases existentes).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await MetadataSchemaInitializer.EnsureAsync(db.Database.GetDbConnection(), logger);
}
```

(El `await` a nivel superior ya esta soportado; el archivo pasa a tener un Main async generado. `ILogger<Program>` esta registrado por defecto en ASP.NET Core.)

- [ ] **Step 2: Reemplazar `EnsureCreated` en el CLI (dos usos)**

En `DataPipeline.Cli/Program.cs`, reemplazar la linea 23:

```csharp
        await ctxMeta.Database.EnsureCreatedAsync();
```

por:

```csharp
        await MetadataSchemaInitializer.EnsureAsync(ctxMeta.Database.GetDbConnection(), null);
```

y la linea 69:

```csharp
    await ctx.Database.EnsureCreatedAsync();
```

por:

```csharp
    await MetadataSchemaInitializer.EnsureAsync(ctx.Database.GetDbConnection(), null);
```

(El CLI no tiene logger → se pasa `null`. `using DataPipeline.Persistence;` y `using Microsoft.EntityFrameworkCore;` ya estan en el archivo.)

- [ ] **Step 3: Build limpio de toda la solucion (0 warnings)**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 4: Suite completa verde (la Api.Tests ejercita el inicializador en el arranque real)**

Run: `dotnet test --nologo`
Expected: todos verdes. En particular, `DataPipeline.Api.Tests` (WebApplicationFactory) arranca la app real, que ahora corre `MetadataSchemaInitializer` — si algo del wiring estuviera mal, esos tests fallarian.

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Api/Program.cs DataPipeline.Cli/Program.cs
git commit -m "refactor(api,cli): usar MetadataSchemaInitializer en vez de EnsureCreated"
```

---

## Notas para quien ejecute

- **Beneficio inmediato:** la `metadata.db` real del usuario (que hoy falla con `no such column: p.ColumnaWatermark`) se pone al dia sola en el proximo arranque de la API — los `ALTER ADD COLUMN` agregan las columnas de watermark sin borrar la base ni perder el DataSource que estaba configurando.
- **Mantenimiento a futuro:** cada columna nueva del modelo EF requiere agregar su linea `ALTER TABLE ... ADD COLUMN` en `MetadataSchemaInitializer.Sentencias`. El test `Crea_esquema_completo_y_permite_roundtrip_de_todas_las_entidades` (drift guard) falla si se olvida.
- **Fuera de alcance:** el serving (`data.db`/`SqliteDataStore`) y las migraciones EF (se conservan, no se aplican en runtime).
