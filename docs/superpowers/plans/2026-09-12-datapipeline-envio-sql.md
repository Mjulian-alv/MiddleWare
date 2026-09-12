# Envío a SQL externo (push desde pipeline) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que un pipeline, al terminar bien, empuje sus filas nuevas a una tabla que DataPipeline crea y mantiene en un SQL Server remoto, sin tocar el ciclo de PULL que ya existe.

**Architecture:** Entidad `Envio` colgada de un pipeline (sin reloj propio). El pipeline prende una bandera `Pendiente` al terminar OK; el tick de 60s que ya existe la consume, lee de la tabla intermedia por cursor `_creado`, escribe por lotes en el destino y reintenta con backoff si falla. El push mueve columnas: no transforma.

**Tech Stack:** .NET 10, EF Core 10 + SQLite (almacén A), `Microsoft.Data.SqlClient` (destino), xUnit, Blazor Server + Syncfusion (Admin).

**Spec:** `docs/Diseño/00038-envio-sql/envio-sql.md` — leerlo antes de empezar. Las maquetas de `docs/Diseño/00038-envio-sql/*.png` son la referencia de las pantallas: **no rediseñar ni agregar campos que no estén en ellas.**

---

## Global Constraints

- **TAREA 00038.** Todo bloque agregado o modificado lleva `// Fecha: 12/09/2026 - TAREA: 00038 - Linea: N - Descripción`. `Linea` = ítem del pedido: **1** = entidad `Envio` y el push; **2** = encadenamiento por bandera `Pendiente`; **3** = pantallas del Admin. En archivo nuevo el comentario va arriba de todo; en una edición, inmediatamente encima del bloque que cambia.
- Identificadores técnicos en inglés; propiedades de dominio en castellano **sin tildes ni ñ**.
- Commits directos a `main`, sin ramas.
- **Gate de 0 warnings:** `dotnet build --no-incremental` tiene que salir limpio antes de cada commit.
- Cambios de esquema del almacén A van en `DataPipeline.Persistence/MetadataSchemaInitializer.cs`. **No** migraciones EF nuevas.
- El Admin es **build-only** por convención del repo: sin tests automatizados de UI.
- Base de partida: **103 tests verdes**. Ninguna tarea puede bajar ese número.
- `MERGE` de T-SQL está prohibido (mismo criterio que TAREA 00026 por SQL Server viejo): `IF EXISTS ... UPDATE ELSE INSERT`.

## Dos decisiones que el spec no cubría, resueltas acá

El spec dejó dos cosas sin decidir. Se resuelven explícitamente, como manda la convención, y quedan marcadas para que el usuario las confirme antes de ejecutar el plan.

**D1 — La tabla destino lleva `_key` y `_creado`, no `_procesado`.** El spec dice que la tabla remota tiene "la misma forma" que la intermedia, pero no cuáles columnas de control viajan. `_key` viaja porque es la clave del upsert. `_creado` viaja porque es **cómo el otro sistema sabe qué es nuevo** ("leé lo que tenga `_creado` mayor a tu última lectura"). `_procesado` **no** viaja: es el ack de nuestros consumidores PULL y no significa nada del otro lado.

**D2 — El tipo SQL de cada columna se deriva del primer lote.** `EnsureTable` necesita tipos y el spec no dice de dónde salen. Se toma el primer valor no nulo de cada columna en el primer lote y se mapea CLR → SQL (tabla en Task 4). Sin valor no nulo, `NVARCHAR(MAX)`. Como la tabla es nuestra y el pipeline ya castea con `ColumnMapTransform`, alcanza. **Limitación conocida:** el tipo queda fijado en la primera corrida; si después cambia el cast del pipeline, la tabla remota hay que borrarla a mano.

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `DataPipeline.Core/Envios/Envio.cs` | **Crear.** Entidad + `EstadoEnvio` derivado + `MaxIntentos` |
| `DataPipeline.Core/Abstractions/IEnvioStore.cs` | **Crear.** Persistencia de envíos, incluida la toma anti-solape |
| `DataPipeline.Core/Abstractions/IDestinoDatos.cs` | **Crear.** Escritura en el destino externo. **No** es `IDataStore`: no lee, no ackea |
| `DataPipeline.Core/Abstractions/IDestinoFactory.cs` | **Crear.** `DataSource` → `IDestinoDatos` |
| `DataPipeline.Core/Pipelines/PipelineRun.cs` | **Modificar.** `EnvioId` nullable |
| `DataPipeline.Core/Abstractions/IHistorialCorridas.cs` | **Modificar.** `IniciarEnvioAsync` |
| `DataPipeline.Core/Abstractions/IDataStore.cs` | **Modificar.** `LeerDesdeAsync` (cursor) |
| `DataPipeline.Persistence/MetadataSchemaInitializer.cs` | **Modificar.** DDL de `Envios` + `ALTER` de `Corridas` |
| `DataPipeline.Persistence/MetadataDbContext.cs` | **Modificar.** `DbSet<Envio>` + mapping |
| `DataPipeline.Persistence/EfEnvioStore.cs` | **Crear.** |
| `DataPipeline.Persistence/EfHistorialCorridas.cs` | **Modificar.** |
| `DataPipeline.Connectors/Stores/SqliteDataStore.cs` | **Modificar.** `LeerDesdeAsync` |
| `DataPipeline.Connectors/Destinos/SqlServerDestinoSql.cs` | **Crear.** Armado de SQL **puro** (testeable sin servidor) |
| `DataPipeline.Connectors/Destinos/SqlServerDestino.cs` | **Crear.** ADO.NET sobre el SQL de arriba |
| `DataPipeline.Connectors/Destinos/DestinoFactory.cs` | **Crear.** |
| `DataPipeline.Engine/EjecutorEnvios.cs` | **Crear.** El barrido completo |
| `DataPipeline.Engine/BackoffEnvios.cs` | **Crear.** Política de reintentos, pura |
| `DataPipeline.Engine/EjecutorProgramado.cs` | **Modificar.** Prende `Pendiente` |
| `DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs` | **Modificar.** Corre envíos en el tick |
| `DataPipeline.Api/Controllers/EnviosController.cs` | **Crear.** |
| `DataPipeline.Api/Program.cs` | **Modificar.** DI |
| `DataPipeline.Admin/Services/EnviosAdminService.cs` | **Crear.** |
| `DataPipeline.Admin/Components/Pages/Envios/Index.razor` | **Crear.** Maqueta `01`/`02` |
| `DataPipeline.Admin/Components/Pages/Envios/Form.razor` | **Crear.** Maqueta `03`/`04` |
| `DataPipeline.Admin/Components/Layout/NavMenu.razor` | **Modificar.** Entrada Envíos |
| `DataPipeline.Admin/Components/Pages/Runs/Index.razor` | **Modificar.** Maqueta `05` |

---

### Task 1: Entidad `Envio` y esquema del almacén A

**Files:**
- Create: `DataPipeline.Core/Envios/Envio.cs`
- Modify: `DataPipeline.Core/Pipelines/PipelineRun.cs`
- Modify: `DataPipeline.Persistence/MetadataSchemaInitializer.cs`
- Modify: `DataPipeline.Persistence/MetadataDbContext.cs`
- Test: `tests/DataPipeline.Persistence.Tests/EnvioSchemaTests.cs`

**Interfaces:**
- Produces: `DataPipeline.Core.Envios.Envio` (propiedades abajo), `EstadoEnvio { AlDia, Pendiente, Reintentando, Fallido, Deshabilitado }`, `Envio.MaxIntentos = 5`, `PipelineRun.EnvioId` (`Guid?`), `MetadataDbContext.Envios`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Persistence.Tests/EnvioSchemaTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: la tabla Envios y la columna
// EnvioId de Corridas se crean con el DDL idempotente, no con migraciones EF.
using DataPipeline.Core.Envios;
using DataPipeline.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class EnvioSchemaTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"envsch_{Guid.NewGuid():N}.db")};Pooling=False";

    private static async Task<List<string>> ColumnasAsync(SqliteConnection con, string tabla)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabla}\");";
        await using var rd = await cmd.ExecuteReaderAsync();
        var cols = new List<string>();
        while (await rd.ReadAsync()) cols.Add(rd.GetString(1));
        return cols;
    }

    [Fact]
    public async Task Crea_la_tabla_Envios_con_todas_sus_columnas()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        var cols = await ColumnasAsync(con, "Envios");

        Assert.Contains("Id", cols);
        Assert.Contains("Nombre", cols);
        Assert.Contains("Habilitado", cols);
        Assert.Contains("PipelineId", cols);
        Assert.Contains("DataSourceId", cols);
        Assert.Contains("TablaDestino", cols);
        Assert.Contains("Pendiente", cols);
        Assert.Contains("InicioEnCurso", cols);
        Assert.Contains("UltimoCreado", cols);
        Assert.Contains("IntentosFallidos", cols);
        Assert.Contains("ProximoIntento", cols);
        Assert.Contains("UltimoEnvio", cols);
        Assert.Contains("UltimoError", cols);
    }

    [Fact]
    public async Task Agrega_EnvioId_a_Corridas()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        Assert.Contains("EnvioId", await ColumnasAsync(con, "Corridas"));
    }

    [Fact]
    public async Task Es_idempotente_sobre_una_base_que_ya_tiene_el_esquema()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        Assert.Contains("Pendiente", await ColumnasAsync(con, "Envios"));
    }

    [Theory]
    [InlineData(true, false, 0, EstadoEnvio.AlDia)]
    [InlineData(true, true, 0, EstadoEnvio.Pendiente)]
    [InlineData(true, true, 2, EstadoEnvio.Reintentando)]
    [InlineData(true, true, 5, EstadoEnvio.Fallido)]
    [InlineData(false, false, 5, EstadoEnvio.Fallido)]
    [InlineData(false, false, 0, EstadoEnvio.Deshabilitado)]
    public void El_estado_se_deriva_de_las_banderas(bool habilitado, bool pendiente, int intentos, EstadoEnvio esperado)
    {
        var envio = new Envio { Habilitado = habilitado, Pendiente = pendiente, IntentosFallidos = intentos };

        Assert.Equal(esperado, envio.Estado);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --filter EnvioSchemaTests`
Expected: FAIL de compilación — `El nombre del tipo o del espacio de nombres 'Envios' no existe`.

- [ ] **Step 3: Crear la entidad**

Crear `DataPipeline.Core/Envios/Envio.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: entidad nueva del almacen A.
// El envio no tiene programacion propia: se cuelga de un pipeline (PipelineId) y hereda su cadencia.
// El disparo es la bandera Pendiente, que el pipeline prende al terminar bien y el tick del scheduler
// consume. UltimoCreado es el cursor sobre la columna _creado de la tabla intermedia.
namespace DataPipeline.Core.Envios;

/// <summary>Estado visible de un envio. Derivado, no persistido.</summary>
public enum EstadoEnvio { AlDia, Pendiente, Reintentando, Fallido, Deshabilitado }

/// <summary>Push del resultado de un pipeline a una tabla nuestra en un SQL Server remoto.</summary>
public sealed class Envio
{
    /// <summary>Tope de reintentos automaticos. Al alcanzarlo el envio queda Fallido y se deshabilita.</summary>
    public const int MaxIntentos = 5;

    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Habilitado { get; set; } = true;

    /// <summary>Pipeline duenno. De el hereda la cadencia: el envio no tiene horario propio.</summary>
    public Guid PipelineId { get; set; }

    /// <summary>Conexion al SQL remoto. Es un DataSource de tipo SqlServer usado como destino.</summary>
    public Guid DataSourceId { get; set; }

    /// <summary>Nombre de la tabla en el servidor remoto. La crea y la mantiene DataPipeline.</summary>
    public string TablaDestino { get; set; } = "";

    /// <summary>Bandera del disparo: hay filas para empujar.</summary>
    public bool Pendiente { get; set; }

    /// <summary>Anti-solape. Null = libre.</summary>
    public DateTime? InicioEnCurso { get; set; }

    /// <summary>Cursor: maximo _creado ya empujado. Avanza solo al terminar el envio completo.</summary>
    public DateTime? UltimoCreado { get; set; }

    public int IntentosFallidos { get; set; }
    public DateTime? ProximoIntento { get; set; }
    public DateTime? UltimoEnvio { get; set; }
    public string? UltimoError { get; set; }

    /// <summary>
    /// Estado para la grilla. Fallido gana sobre Deshabilitado porque al agotar los reintentos el
    /// envio se deshabilita solo: hay que poder distinguirlo del que alguien apago a mano.
    /// </summary>
    public EstadoEnvio Estado =>
        IntentosFallidos >= MaxIntentos ? EstadoEnvio.Fallido
        : !Habilitado ? EstadoEnvio.Deshabilitado
        : IntentosFallidos > 0 ? EstadoEnvio.Reintentando
        : Pendiente ? EstadoEnvio.Pendiente
        : EstadoEnvio.AlDia;
}
```

- [ ] **Step 4: Agregar `EnvioId` al historial**

En `DataPipeline.Core/Pipelines/PipelineRun.cs`, agregar dentro de la clase `PipelineRun`, después de `PipelineId`:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: una sola tabla de
    // historial. Null = corrida de pipeline; con valor = corrida de envio de ese pipeline.
    public Guid? EnvioId { get; set; }
```

- [ ] **Step 5: Agregar el DDL**

En `DataPipeline.Persistence/MetadataSchemaInitializer.cs`, dentro del array `Sentencias`, **antes** del bloque de comentario `// Columnas agregadas despues del esquema inicial`:

```csharp
        // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: tabla Envios.
        """
        CREATE TABLE IF NOT EXISTS "Envios" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Envios" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Habilitado"       INTEGER NOT NULL DEFAULT 1,
            "PipelineId"       TEXT    NOT NULL,
            "DataSourceId"     TEXT    NOT NULL,
            "TablaDestino"     TEXT    NOT NULL,
            "Pendiente"        INTEGER NOT NULL DEFAULT 0,
            "InicioEnCurso"    TEXT    NULL,
            "UltimoCreado"     TEXT    NULL,
            "IntentosFallidos" INTEGER NOT NULL DEFAULT 0,
            "ProximoIntento"   TEXT    NULL,
            "UltimoEnvio"      TEXT    NULL,
            "UltimoError"      TEXT    NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Envios_Nombre"     ON "Envios" ("Nombre")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Envios_PipelineId" ON "Envios" ("PipelineId")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Envios_Pendiente"  ON "Envios" ("Pendiente")""",
```

Y al final del array, junto a los demás `ALTER`:

```csharp
        // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: EnvioId distingue
        // la corrida de envio de la de pipeline en la misma tabla de historial.
        """ALTER TABLE "Corridas" ADD COLUMN "EnvioId" TEXT NULL""",
```

- [ ] **Step 6: Mapear en EF**

En `DataPipeline.Persistence/MetadataDbContext.cs`, agregar el `using` y el `DbSet` junto a los otros:

```csharp
using DataPipeline.Core.Envios;
```

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
    public DbSet<Envio> Envios => Set<Envio>();
```

Y al final de `OnModelCreating`, después del bloque de `origen`:

```csharp
        // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo. Estado es derivado,
        // no tiene columna: se ignora para que EF no intente materializarlo.
        var envio = b.Entity<Envio>();
        envio.HasKey(e => e.Id);
        envio.HasIndex(e => e.Nombre).IsUnique();
        envio.HasIndex(e => e.PipelineId);
        envio.Ignore(e => e.Estado);
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --filter EnvioSchemaTests`
Expected: PASS — 9 tests (3 de esquema + 6 del `[Theory]`).

- [ ] **Step 8: Verificar que no se rompió nada**

Run: `dotnet build --no-incremental` → 0 warnings, 0 errors.
Run: `dotnet test` → 112 tests verdes (103 previos + 9 nuevos).

- [ ] **Step 9: Commit**

```bash
git add DataPipeline.Core/Envios/Envio.cs DataPipeline.Core/Pipelines/PipelineRun.cs DataPipeline.Persistence/MetadataSchemaInitializer.cs DataPipeline.Persistence/MetadataDbContext.cs tests/DataPipeline.Persistence.Tests/EnvioSchemaTests.cs
git commit -m "feat(core): entidad Envio + esquema del almacen A (TAREA 00038)"
```

---

### Task 2: `IEnvioStore` con la toma anti-solape

**Files:**
- Create: `DataPipeline.Core/Abstractions/IEnvioStore.cs`
- Create: `DataPipeline.Persistence/EfEnvioStore.cs`
- Test: `tests/DataPipeline.Persistence.Tests/EnvioStoreTests.cs`

**Interfaces:**
- Consumes: `Envio`, `MetadataDbContext.Envios` (Task 1).
- Produces: `IEnvioStore` con `GetByIdAsync`, `GetAllAsync`, `GetByPipelineAsync`, `UpsertAsync`, `DeleteAsync`, `MarcarPendientesAsync(Guid pipelineId, ...)→Task<int>`, `TomarPendientesAsync(DateTime ahora, ...)→Task<IReadOnlyList<Envio>>`, `LiberarAsync(Guid id, ...)`, `RegistrarExitoAsync(Guid id, DateTime ultimoCreado, DateTime fin, ...)`, `RegistrarFalloAsync(Guid id, string error, DateTime? proximoIntento, bool deshabilitar, ...)`, `ReactivarAsync(Guid id, ...)`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Persistence.Tests/EnvioStoreTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: persistencia de envios.
// El test de la toma anti-solape es el que importa: dos barridos concurrentes no pueden llevarse
// el mismo envio.
using DataPipeline.Core.Envios;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class EnvioStoreTests
{
    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"envsto_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static Envio NuevoEnvio(Guid pipelineId, string nombre = "e1") => new()
    {
        Id = Guid.NewGuid(), Nombre = nombre, Habilitado = true,
        PipelineId = pipelineId, DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
    };

    [Fact]
    public async Task Marcar_pendientes_prende_solo_los_envios_habilitados_de_ese_pipeline()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var pipeA = Guid.NewGuid();
        var pipeB = Guid.NewGuid();

        var deA = NuevoEnvio(pipeA, "a");
        var apagado = NuevoEnvio(pipeA, "apagado");
        apagado.Habilitado = false;
        var deB = NuevoEnvio(pipeB, "b");
        foreach (var e in new[] { deA, apagado, deB }) await store.UpsertAsync(e, CancellationToken.None);

        var n = await store.MarcarPendientesAsync(pipeA, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.True((await store.GetByIdAsync(deA.Id, CancellationToken.None))!.Pendiente);
        Assert.False((await store.GetByIdAsync(apagado.Id, CancellationToken.None))!.Pendiente);
        Assert.False((await store.GetByIdAsync(deB.Id, CancellationToken.None))!.Pendiente);
    }

    [Fact]
    public async Task Tomar_pendientes_devuelve_el_envio_y_lo_deja_en_curso()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);

        var tomados = await store.TomarPendientesAsync(ahora, CancellationToken.None);

        Assert.Single(tomados);
        Assert.Equal(ahora, (await store.GetByIdAsync(e.Id, CancellationToken.None))!.InicioEnCurso);
    }

    [Fact]
    public async Task Tomar_pendientes_no_se_lleva_dos_veces_el_mismo_envio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);

        var primero = await store.TomarPendientesAsync(ahora, CancellationToken.None);
        var segundo = await store.TomarPendientesAsync(ahora.AddSeconds(60), CancellationToken.None);

        Assert.Single(primero);
        Assert.Empty(segundo);
    }

    [Fact]
    public async Task Tomar_pendientes_respeta_el_ProximoIntento_que_todavia_no_vencio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.ProximoIntento = ahora.AddMinutes(5);
        await store.UpsertAsync(e, CancellationToken.None);

        Assert.Empty(await store.TomarPendientesAsync(ahora, CancellationToken.None));
        Assert.Single(await store.TomarPendientesAsync(ahora.AddMinutes(6), CancellationToken.None));
    }

    [Fact]
    public async Task Registrar_exito_avanza_el_cursor_y_apaga_la_bandera()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.IntentosFallidos = 3;
        e.UltimoError = "algo";
        await store.UpsertAsync(e, CancellationToken.None);
        var cursor = new DateTime(2026, 9, 12, 3, 3, 58);
        var fin = new DateTime(2026, 9, 12, 3, 4, 2);

        await store.RegistrarExitoAsync(e.Id, cursor, fin, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.Equal(cursor, r.UltimoCreado);
        Assert.Equal(fin, r.UltimoEnvio);
        Assert.False(r.Pendiente);
        Assert.Null(r.InicioEnCurso);
        Assert.Equal(0, r.IntentosFallidos);
        Assert.Null(r.UltimoError);
        Assert.Equal(EstadoEnvio.AlDia, r.Estado);
    }

    [Fact]
    public async Task Registrar_fallo_suma_intento_y_deja_la_bandera_prendida()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var proximo = new DateTime(2026, 9, 12, 3, 5, 0);

        await store.RegistrarFalloAsync(e.Id, "no conecta", proximo, false, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.Equal(1, r.IntentosFallidos);
        Assert.Equal("no conecta", r.UltimoError);
        Assert.Equal(proximo, r.ProximoIntento);
        Assert.True(r.Pendiente);
        Assert.Null(r.InicioEnCurso);
        Assert.True(r.Habilitado);
    }

    [Fact]
    public async Task Registrar_fallo_con_deshabilitar_apaga_el_envio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.IntentosFallidos = Envio.MaxIntentos - 1;
        await store.UpsertAsync(e, CancellationToken.None);

        await store.RegistrarFalloAsync(e.Id, "login failed", null, true, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.False(r.Habilitado);
        Assert.Equal(Envio.MaxIntentos, r.IntentosFallidos);
        Assert.Equal(EstadoEnvio.Fallido, r.Estado);
    }

    [Fact]
    public async Task Reactivar_limpia_los_intentos_y_vuelve_a_habilitar()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Habilitado = false;
        e.IntentosFallidos = Envio.MaxIntentos;
        e.UltimoError = "login failed";
        await store.UpsertAsync(e, CancellationToken.None);

        await store.ReactivarAsync(e.Id, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.True(r.Habilitado);
        Assert.Equal(0, r.IntentosFallidos);
        Assert.Null(r.ProximoIntento);
        Assert.True(r.Pendiente);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --filter EnvioStoreTests`
Expected: FAIL de compilación — `EfEnvioStore` no existe.

- [ ] **Step 3: Crear la interfaz**

Crear `DataPipeline.Core/Abstractions/IEnvioStore.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: persistencia de envios.
using DataPipeline.Core.Envios;

namespace DataPipeline.Core.Abstractions;

/// <summary>Envios del almacen A. La toma de pendientes es atomica (anti-solape).</summary>
public interface IEnvioStore
{
    Task<Envio?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Envio>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Envio>> GetByPipelineAsync(Guid pipelineId, CancellationToken ct);
    Task UpsertAsync(Envio envio, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Prende Pendiente en los envios habilitados del pipeline. Devuelve cuantos marco.</summary>
    Task<int> MarcarPendientesAsync(Guid pipelineId, CancellationToken ct);

    /// <summary>
    /// Toma los envios listos para correr y los deja EnCurso en la misma operacion. Solo devuelve
    /// los que efectivamente gano: un envio ya tomado por otro barrido no vuelve a salir.
    /// </summary>
    Task<IReadOnlyList<Envio>> TomarPendientesAsync(DateTime ahora, CancellationToken ct);

    /// <summary>Suelta el EnCurso sin tocar nada mas (para una cancelacion limpia).</summary>
    Task LiberarAsync(Guid id, CancellationToken ct);

    Task RegistrarExitoAsync(Guid id, DateTime ultimoCreado, DateTime fin, CancellationToken ct);
    Task RegistrarFalloAsync(Guid id, string error, DateTime? proximoIntento, bool deshabilitar, CancellationToken ct);

    /// <summary>Vuelve a habilitar un envio fallido y lo deja pendiente (boton Reintentar).</summary>
    Task ReactivarAsync(Guid id, CancellationToken ct);
}
```

- [ ] **Step 4: Implementar el store**

Crear `DataPipeline.Persistence/EfEnvioStore.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
// TomarPendientesAsync usa un UPDATE condicional por fila (ExecuteUpdateAsync con InicioEnCurso == null
// en el WHERE) en vez de SELECT + UPDATE: solo se lleva el envio quien gana la condicion, asi dos
// barridos concurrentes no pueden tomar el mismo.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using Microsoft.EntityFrameworkCore;

namespace DataPipeline.Persistence;

public sealed class EfEnvioStore : IEnvioStore
{
    private readonly MetadataDbContext _db;
    public EfEnvioStore(MetadataDbContext db) => _db = db;

    public Task<Envio?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Envios.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Envio>> GetAllAsync(CancellationToken ct) =>
        await _db.Envios.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(ct);

    public async Task<IReadOnlyList<Envio>> GetByPipelineAsync(Guid pipelineId, CancellationToken ct) =>
        await _db.Envios.AsNoTracking().Where(e => e.PipelineId == pipelineId).ToListAsync(ct);

    public async Task UpsertAsync(Envio envio, CancellationToken ct)
    {
        var existe = await _db.Envios.AnyAsync(e => e.Id == envio.Id, ct);
        if (existe) _db.Envios.Update(envio);
        else _db.Envios.Add(envio);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var envio = await _db.Envios.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null) return false;
        _db.Envios.Remove(envio);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> MarcarPendientesAsync(Guid pipelineId, CancellationToken ct) =>
        _db.Envios
            .Where(e => e.PipelineId == pipelineId && e.Habilitado)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Pendiente, true), ct);

    public async Task<IReadOnlyList<Envio>> TomarPendientesAsync(DateTime ahora, CancellationToken ct)
    {
        var candidatos = await _db.Envios.AsNoTracking()
            .Where(e => e.Habilitado && e.Pendiente && e.InicioEnCurso == null
                     && (e.ProximoIntento == null || e.ProximoIntento <= ahora))
            .ToListAsync(ct);

        var tomados = new List<Envio>(candidatos.Count);
        foreach (var c in candidatos)
        {
            var filas = await _db.Envios
                .Where(e => e.Id == c.Id && e.InicioEnCurso == null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.InicioEnCurso, ahora), ct);
            if (filas != 1) continue;
            c.InicioEnCurso = ahora;
            tomados.Add(c);
        }
        return tomados;
    }

    public Task LiberarAsync(Guid id, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.InicioEnCurso, (DateTime?)null), ct);

    public Task RegistrarExitoAsync(Guid id, DateTime ultimoCreado, DateTime fin, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.UltimoCreado, ultimoCreado)
                .SetProperty(e => e.UltimoEnvio, (DateTime?)fin)
                .SetProperty(e => e.Pendiente, false)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.IntentosFallidos, 0)
                .SetProperty(e => e.ProximoIntento, (DateTime?)null)
                .SetProperty(e => e.UltimoError, (string?)null), ct);

    public Task RegistrarFalloAsync(Guid id, string error, DateTime? proximoIntento, bool deshabilitar, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IntentosFallidos, e => e.IntentosFallidos + 1)
                .SetProperty(e => e.UltimoError, error)
                .SetProperty(e => e.ProximoIntento, proximoIntento)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.Habilitado, e => deshabilitar ? false : e.Habilitado), ct);

    public Task ReactivarAsync(Guid id, CancellationToken ct) =>
        _db.Envios.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Habilitado, true)
                .SetProperty(e => e.IntentosFallidos, 0)
                .SetProperty(e => e.ProximoIntento, (DateTime?)null)
                .SetProperty(e => e.InicioEnCurso, (DateTime?)null)
                .SetProperty(e => e.Pendiente, true), ct);
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --filter EnvioStoreTests`
Expected: PASS — 8 tests.

- [ ] **Step 6: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 120 verdes.

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Core/Abstractions/IEnvioStore.cs DataPipeline.Persistence/EfEnvioStore.cs tests/DataPipeline.Persistence.Tests/EnvioStoreTests.cs
git commit -m "feat(persistence): IEnvioStore con toma anti-solape (TAREA 00038)"
```

---

### Task 3: Lectura por cursor en `IDataStore`

**Files:**
- Modify: `DataPipeline.Core/Abstractions/IDataStore.cs`
- Modify: `DataPipeline.Connectors/Stores/SqliteDataStore.cs`
- Test: `tests/DataPipeline.Tests/SqliteDataStoreCursorTests.cs`

**Interfaces:**
- Produces: `IDataStore.LeerDesdeAsync(string table, DateTime? creadoDesde, int page, int pageSize, CancellationToken ct) → Task<RecordBatch>`. Devuelve las filas con `_creado > creadoDesde` (todas si es `null`), ordenadas por `_creado, _key`, **sin filtrar por `_procesado`**.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Tests/SqliteDataStoreCursorTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor.
// _creado se refresca en el upsert de Reemplazar, asi que sirve de cursor de cambios: una fila
// modificada vuelve a entrar en el barrido. Y no se filtra por _procesado, para no competir con
// los consumidores PULL.
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

    private static DateTime Creado(Record fila) => Convert.ToDateTime(fila.Values[ServingColumns.Creado]);

    [Fact]
    public async Task Sin_cursor_devuelve_todo()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var lote = await store.LeerDesdeAsync("t", null, 1, 10, CancellationToken.None);

        Assert.Equal(2, lote.Rows.Count);
    }

    [Fact]
    public async Task Con_cursor_devuelve_solo_lo_escrito_despues()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var primera = await store.LeerDesdeAsync("t", null, 1, 10, CancellationToken.None);
        var cursor = Creado(primera.Rows[0]);

        await Task.Delay(1100);
        await store.WriteBatchAsync("t", Lote((2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var nuevas = await store.LeerDesdeAsync("t", cursor, 1, 10, CancellationToken.None);

        Assert.Single(nuevas.Rows);
        Assert.Equal(2L, Convert.ToInt64(nuevas.Rows[0].Values["id"]));
    }

    [Fact]
    public async Task Una_fila_modificada_vuelve_a_entrar_en_el_barrido()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Reemplazar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var primera = await store.LeerDesdeAsync("t", null, 1, 10, CancellationToken.None);
        var cursor = Creado(primera.Rows[0]);

        await Task.Delay(1100);
        await store.WriteBatchAsync("t", Lote((1, 999)), Clave, ModoEscritura.Reemplazar, CancellationToken.None);

        var nuevas = await store.LeerDesdeAsync("t", cursor, 1, 10, CancellationToken.None);

        Assert.Single(nuevas.Rows);
        Assert.Equal(999L, Convert.ToInt64(nuevas.Rows[0].Values["monto"]));
    }

    [Fact]
    public async Task No_filtra_por_procesado()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var todas = await store.LeerDesdeAsync("t", null, 1, 10, CancellationToken.None);
        var keys = todas.Rows.Select(r => (string)r.Values[ServingColumns.Key]!).ToList();
        await store.MarcarProcesadasAsync("t", keys, CancellationToken.None);

        var despues = await store.LeerDesdeAsync("t", null, 1, 10, CancellationToken.None);

        Assert.Equal(2, despues.Rows.Count);
    }

    [Fact]
    public async Task Pagina_sin_repetir_ni_saltear()
    {
        var store = new SqliteDataStore(NuevaDb());
        await store.EnsureTableAsync("t", Cols, ModoEscritura.Agregar, CancellationToken.None);
        await store.WriteBatchAsync("t", Lote((1, 1), (2, 2), (3, 3), (4, 4), (5, 5)), Clave,
            ModoEscritura.Agregar, CancellationToken.None);

        var p1 = await store.LeerDesdeAsync("t", null, 1, 2, CancellationToken.None);
        var p2 = await store.LeerDesdeAsync("t", null, 2, 2, CancellationToken.None);
        var p3 = await store.LeerDesdeAsync("t", null, 3, 2, CancellationToken.None);

        var ids = p1.Rows.Concat(p2.Rows).Concat(p3.Rows)
            .Select(r => Convert.ToInt64(r.Values["id"])).ToList();

        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }
}
```

> **Nota sobre los `Task.Delay(1100)`:** `_creado` se guarda con resolución de segundos en SQLite. Sin la espera, las dos escrituras caen en el mismo instante y el cursor no las separa. Es lento pero es la única forma de testear el cursor de verdad.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Tests --filter SqliteDataStoreCursorTests`
Expected: FAIL de compilación — `LeerDesdeAsync` no existe en `IDataStore`.

- [ ] **Step 3: Agregar el método a la interfaz**

En `DataPipeline.Core/Abstractions/IDataStore.cs`, dentro de la interfaz, después de `ReadPagedAsync`:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor para el
    // push. A diferencia de ReadPagedAsync NO filtra por _procesado: el push es un consumidor con su
    // propia memoria (UltimoCreado) y no le puede pisar las filas a los consumidores PULL.
    Task<RecordBatch> LeerDesdeAsync(string table, DateTime? creadoDesde, int page, int pageSize, CancellationToken ct);
```

- [ ] **Step 4: Implementar en `SqliteDataStore`**

En `DataPipeline.Connectors/Stores/SqliteDataStore.cs`, agregar después de `ReadPagedAsync`:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor.
    // Orden por (_creado, _key) y no solo por _creado: con resolucion de segundos hay empates, y sin
    // un desempate estable el paginado puede repetir o saltear filas entre paginas.
    public async Task<RecordBatch> LeerDesdeAsync(string table, DateTime? creadoDesde, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var filtro = creadoDesde is null ? "" : $" WHERE {Quote(ServingColumns.Creado)} > $desde";
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)}{filtro} " +
                          $"ORDER BY {Quote(ServingColumns.Creado)}, {Quote(ServingColumns.Key)} " +
                          "LIMIT $take OFFSET $skip;";
        if (creadoDesde is not null)
        {
            var pDesde = cmd.CreateParameter();
            pDesde.ParameterName = "$desde";
            pDesde.Value = creadoDesde.Value;
            cmd.Parameters.Add(pDesde);
        }
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
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Tests --filter SqliteDataStoreCursorTests`
Expected: PASS — 5 tests.

- [ ] **Step 6: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 125 verdes.

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Core/Abstractions/IDataStore.cs DataPipeline.Connectors/Stores/SqliteDataStore.cs tests/DataPipeline.Tests/SqliteDataStoreCursorTests.cs
git commit -m "feat(connectors): IDataStore.LeerDesdeAsync, lectura por cursor _creado (TAREA 00038)"
```

---

### Task 4: El destino SQL Server

**Files:**
- Create: `DataPipeline.Core/Abstractions/IDestinoDatos.cs`
- Create: `DataPipeline.Core/Abstractions/IDestinoFactory.cs`
- Create: `DataPipeline.Connectors/Destinos/SqlServerDestinoSql.cs`
- Create: `DataPipeline.Connectors/Destinos/SqlServerDestino.cs`
- Create: `DataPipeline.Connectors/Destinos/DestinoFactory.cs`
- Test: `tests/DataPipeline.Tests/SqlServerDestinoSqlTests.cs`

**Interfaces:**
- Consumes: `RecordBatch`, `ServingColumns` (ya existen).
- Produces:
  - `ColumnaDestino(string Nombre, Type? TipoClr)` — record en `DataPipeline.Core.Abstractions`.
  - `IDestinoDatos.EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct)` y `IDestinoDatos.WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct) → Task<long>` (filas escritas).
  - `IDestinoFactory.Crear(DataSource ds, IEncryptionService enc) → IDestinoDatos`.
  - `SqlServerDestinoSql.TipoSql(Type?) → string`, `.CrearTabla(string, IReadOnlyList<ColumnaDestino>) → string`, `.Upsert(string, IReadOnlyList<string>) → string`, `.Partes(string) → (string Esquema, string Tabla)`.

> **No hay SQL Server en el entorno de desarrollo.** Por eso el armado del SQL vive en una clase **pura** (`SqlServerDestinoSql`) que se testea sin servidor, y `SqlServerDestino` queda como una cáscara fina de ADO.NET sobre ella. La ruta real contra un SQL Server queda **sin probar en vivo**: anotarlo en el Historial, mismo criterio que TAREA 00026.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Tests/SqlServerDestinoSqlTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: el armado del SQL se testea
// puro, sin servidor. MERGE esta prohibido (SQL Server viejo, mismo criterio que TAREA 00026).
using DataPipeline.Connectors.Destinos;
using DataPipeline.Core.Abstractions;
using Xunit;

namespace DataPipeline.Tests;

public class SqlServerDestinoSqlTests
{
    private static readonly ColumnaDestino[] Columnas =
    {
        new("id", typeof(long)),
        new("monto", typeof(decimal)),
        new("fecha", typeof(DateTime)),
        new("activo", typeof(bool)),
        new("nombre", typeof(string)),
        new("sinDato", null)
    };

    [Theory]
    [InlineData(typeof(int), "BIGINT")]
    [InlineData(typeof(long), "BIGINT")]
    [InlineData(typeof(decimal), "DECIMAL(38,10)")]
    [InlineData(typeof(double), "FLOAT")]
    [InlineData(typeof(bool), "BIT")]
    [InlineData(typeof(DateTime), "DATETIME2")]
    [InlineData(typeof(string), "NVARCHAR(MAX)")]
    [InlineData(null, "NVARCHAR(MAX)")]
    public void Mapea_el_tipo_clr_al_tipo_sql(Type? clr, string esperado) =>
        Assert.Equal(esperado, SqlServerDestinoSql.TipoSql(clr));

    [Fact]
    public void Crear_tabla_es_idempotente_y_agrega_las_columnas_de_control()
    {
        var sql = SqlServerDestinoSql.CrearTabla("dbo.mw_x", Columnas);

        Assert.Contains("IF OBJECT_ID(N'[dbo].[mw_x]', N'U') IS NULL", sql);
        Assert.Contains("[id] BIGINT NULL", sql);
        Assert.Contains("[monto] DECIMAL(38,10) NULL", sql);
        Assert.Contains("[sinDato] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("[_key] NVARCHAR(450) NOT NULL", sql);
        Assert.Contains("[_creado] DATETIME2 NOT NULL", sql);
        Assert.Contains("CONSTRAINT [PK_mw_x] PRIMARY KEY ([_key])", sql);
    }

    [Fact]
    public void Crear_tabla_no_lleva_la_columna_procesado()
    {
        // _procesado es el ack de NUESTROS consumidores PULL: del otro lado no significa nada.
        Assert.DoesNotContain("_procesado", SqlServerDestinoSql.CrearTabla("dbo.mw_x", Columnas));
    }

    [Fact]
    public void Upsert_usa_IF_EXISTS_y_no_MERGE()
    {
        var sql = SqlServerDestinoSql.Upsert("dbo.mw_x", new[] { "id", "monto", "_key", "_creado" });

        Assert.Contains("IF EXISTS", sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("ELSE", sql);
        Assert.Contains("INSERT", sql);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upsert_matchea_por_key_y_no_lo_pisa_en_el_update()
    {
        var sql = SqlServerDestinoSql.Upsert("dbo.mw_x", new[] { "id", "_key", "_creado" });

        Assert.Contains("WHERE [_key] = @p_key", sql);
        Assert.Contains("[id] = @p0", sql);
        Assert.DoesNotContain("SET [_key] =", sql);
    }

    [Theory]
    [InlineData("dbo.mw_x", "dbo", "mw_x")]
    [InlineData("mw_x", "dbo", "mw_x")]
    [InlineData("[ventas].[mw_x]", "ventas", "mw_x")]
    public void Parte_el_nombre_en_esquema_y_tabla(string entrada, string esquema, string tabla)
    {
        var partes = SqlServerDestinoSql.Partes(entrada);

        Assert.Equal(esquema, partes.Esquema);
        Assert.Equal(tabla, partes.Tabla);
    }

    [Fact]
    public void Escapa_los_corchetes_del_identificador()
    {
        var sql = SqlServerDestinoSql.CrearTabla("dbo.mal]nombre", Columnas);

        Assert.Contains("[mal]]nombre]", sql);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Tests --filter SqlServerDestinoSqlTests`
Expected: FAIL de compilación — `SqlServerDestinoSql` no existe.

- [ ] **Step 3: Crear las abstracciones**

Crear `DataPipeline.Core/Abstractions/IDestinoDatos.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
// Deliberadamente NO es un IDataStore: un destino externo solo se escribe. No se lee, no se ackea,
// no tiene _procesado. Meterlo en IDataStore obligaria a implementar tres metodos sin sentido.
using DataPipeline.Core.Records;

namespace DataPipeline.Core.Abstractions;

/// <summary>Columna a crear en el destino. TipoClr null = no se pudo inferir, va como texto.</summary>
public sealed record ColumnaDestino(string Nombre, Type? TipoClr);

/// <summary>Escritura en una tabla nuestra alojada en un servidor externo.</summary>
public interface IDestinoDatos
{
    Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct);

    /// <summary>Upsert del lote por _key. Devuelve las filas escritas.</summary>
    Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct);
}
```

Crear `DataPipeline.Core/Abstractions/IDestinoFactory.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: mismo molde que ISourceFactory.
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve el destino a partir del DataSource configurado en el envio.</summary>
public interface IDestinoFactory
{
    IDestinoDatos Crear(DataSource ds, IEncryptionService enc);
}
```

- [ ] **Step 4: Implementar el armado de SQL**

Crear `DataPipeline.Connectors/Destinos/SqlServerDestinoSql.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: armado de SQL puro.
// Vive separado del ADO.NET para poder testearlo sin un SQL Server a mano (no hay uno en el entorno
// de desarrollo). Sin MERGE, por el SQL Server viejo: mismo criterio que TAREA 00026.
using System.Text;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Destinos;

public static class SqlServerDestinoSql
{
    /// <summary>
    /// Tipo SQL para el tipo CLR observado en el primer lote. La tabla es nuestra, asi que se elige
    /// ancho: BIGINT y DECIMAL(38,10) entran cualquier valor que venga del pipeline sin desbordar.
    /// Todo lo que no se reconoce va como texto.
    /// </summary>
    public static string TipoSql(Type? clr) => clr switch
    {
        null => "NVARCHAR(MAX)",
        _ when clr == typeof(byte) || clr == typeof(short) || clr == typeof(int) || clr == typeof(long) => "BIGINT",
        _ when clr == typeof(decimal) => "DECIMAL(38,10)",
        _ when clr == typeof(float) || clr == typeof(double) => "FLOAT",
        _ when clr == typeof(bool) => "BIT",
        _ when clr == typeof(DateTime) || clr == typeof(DateTimeOffset) => "DATETIME2",
        _ when clr == typeof(Guid) => "UNIQUEIDENTIFIER",
        _ => "NVARCHAR(MAX)"
    };

    /// <summary>Parte "esquema.tabla" (con o sin corchetes). Sin esquema explicito, dbo.</summary>
    public static (string Esquema, string Tabla) Partes(string nombre)
    {
        var limpio = nombre.Replace("[", "").Replace("]", "");
        var punto = limpio.IndexOf('.');
        return punto < 0
            ? ("dbo", limpio)
            : (limpio[..punto], limpio[(punto + 1)..]);
    }

    private static string Q(string id) => "[" + id.Replace("]", "]]") + "]";

    /// <summary>
    /// CREATE TABLE idempotente. Las columnas de negocio van NULL (el pipeline puede traer nulos);
    /// _key es la PK y _creado es la marca que el sistema destino usa para saber que es nuevo.
    /// _procesado NO viaja: es el ack de nuestros consumidores PULL.
    /// </summary>
    public static string CrearTabla(string tabla, IReadOnlyList<ColumnaDestino> columnas)
    {
        var (esquema, nombre) = Partes(tabla);
        var sb = new StringBuilder();
        sb.AppendLine($"IF OBJECT_ID(N'{Q(esquema)}.{Q(nombre)}', N'U') IS NULL");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    CREATE TABLE {Q(esquema)}.{Q(nombre)} (");
        foreach (var c in columnas)
            sb.AppendLine($"        {Q(c.Nombre)} {TipoSql(c.TipoClr)} NULL,");
        sb.AppendLine($"        {Q(ServingColumns.Key)} NVARCHAR(450) NOT NULL,");
        sb.AppendLine($"        {Q(ServingColumns.Creado)} DATETIME2 NOT NULL,");
        sb.AppendLine($"        CONSTRAINT {Q("PK_" + nombre)} PRIMARY KEY ({Q(ServingColumns.Key)})");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        return sb.ToString();
    }

    /// <summary>
    /// Upsert de una fila por _key. Las columnas llegan con los nombres de la tabla; el parametro de
    /// cada una es @p{indice}, salvo _key que es @p_key para poder referenciarlo en los dos WHERE.
    /// </summary>
    public static string Upsert(string tabla, IReadOnlyList<string> columnas)
    {
        var (esquema, nombre) = Partes(tabla);
        var destino = $"{Q(esquema)}.{Q(nombre)}";

        // Diccionario armado por indice: IReadOnlyList<T> no tiene IndexOf, y buscarlo por linea
        // seria O(n^2) sobre cada lote.
        var param = new Dictionary<string, string>(columnas.Count);
        for (int i = 0; i < columnas.Count; i++)
            param[columnas[i]] = columnas[i] == ServingColumns.Key ? "@p_key" : "@p" + i;

        var sets = columnas
            .Where(c => c != ServingColumns.Key)
            .Select(c => $"{Q(c)} = {param[c]}");

        var cols = string.Join(", ", columnas.Select(Q));
        var vals = string.Join(", ", columnas.Select(c => param[c]));

        var sb = new StringBuilder();
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM {destino} WHERE {Q(ServingColumns.Key)} = @p_key)");
        sb.AppendLine($"    UPDATE {destino} SET {string.Join(", ", sets)} WHERE {Q(ServingColumns.Key)} = @p_key;");
        sb.AppendLine("ELSE");
        sb.AppendLine($"    INSERT INTO {destino} ({cols}) VALUES ({vals});");
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Tests --filter SqlServerDestinoSqlTests`
Expected: PASS — 16 tests (8 del `[Theory]` de tipos + 3 del de `Partes` + 5 `[Fact]`).

- [ ] **Step 6: Implementar el destino ADO.NET**

Crear `DataPipeline.Connectors/Destinos/SqlServerDestino.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: cascara fina de ADO.NET sobre
// SqlServerDestinoSql. Toda la logica de armado vive alla, testeada sin servidor. El lote va en una
// transaccion: o entra completo o no entra, para que el cursor no avance sobre algo a medias.
using System.Data;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Destinos;

public sealed class SqlServerDestino : IDestinoDatos
{
    private readonly string _connectionString;
    public SqlServerDestino(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct)
    {
        if (columnas.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columnas));

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(SqlServerDestinoSql.CrearTabla(tabla, columnas), con);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct)
    {
        if (lote.Rows.Count == 0) return 0;

        // Se escriben las columnas de negocio + _key + _creado. _procesado se descarta.
        var columnas = lote.Columns.Where(c => c != ServingColumns.Procesado).ToArray();
        var sql = SqlServerDestinoSql.Upsert(tabla, columnas);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand(sql, con, tx);

        for (int i = 0; i < columnas.Length; i++)
        {
            var nombre = columnas[i] == ServingColumns.Key ? "@p_key" : "@p" + i;
            if (!cmd.Parameters.Contains(nombre))
                cmd.Parameters.Add(new SqlParameter(nombre, DBNull.Value));
        }

        long escritas = 0;
        foreach (var fila in lote.Rows)
        {
            for (int i = 0; i < columnas.Length; i++)
            {
                var nombre = columnas[i] == ServingColumns.Key ? "@p_key" : "@p" + i;
                fila.Values.TryGetValue(columnas[i], out var valor);
                cmd.Parameters[nombre].Value = valor ?? DBNull.Value;
            }
            await cmd.ExecuteNonQueryAsync(ct);
            escritas++;
        }

        await tx.CommitAsync(ct);
        return escritas;
    }
}
```

Crear `DataPipeline.Connectors/Destinos/DestinoFactory.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: mismo molde que SourceFactory.
// Hoy solo SqlServer; el dia que haya otro destino se agrega un case, no se reescribe nada.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;

namespace DataPipeline.Connectors.Destinos;

public sealed class DestinoFactory : IDestinoFactory
{
    public IDestinoDatos Crear(DataSource ds, IEncryptionService enc)
    {
        if (ds.Tipo != TipoDataSource.SqlServer)
            throw new NotSupportedException(
                $"El origen '{ds.Nombre}' es de tipo {ds.Tipo}: solo SqlServer sirve como destino de un envio.");

        var cs = enc.Decrypt(ds.ConnectionString ?? "");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"El origen '{ds.Nombre}' no tiene cadena de conexion.");

        return new SqlServerDestino(cs);
    }
}
```

> **Verificar la firma de `IEncryptionService`** antes de escribir esto: si el método no se llama `Decrypt`, usar el que exista en `DataPipeline.Core/Security/IEncryptionService.cs` y copiar el uso de `PipelineHydrator.cs:39`.

- [ ] **Step 7: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 141 verdes.

- [ ] **Step 8: Commit**

```bash
git add DataPipeline.Core/Abstractions/IDestinoDatos.cs DataPipeline.Core/Abstractions/IDestinoFactory.cs DataPipeline.Connectors/Destinos/ tests/DataPipeline.Tests/SqlServerDestinoSqlTests.cs
git commit -m "feat(connectors): destino SQL Server con upsert sin MERGE (TAREA 00038)"
```

---

### Task 5: Backoff de reintentos

**Files:**
- Create: `DataPipeline.Engine/BackoffEnvios.cs`
- Test: `tests/DataPipeline.Tests/BackoffEnviosTests.cs`

**Interfaces:**
- Produces: `BackoffEnvios.Calcular(int intentosFallidosPrevios, DateTime ahora) → (DateTime? ProximoIntento, bool Deshabilitar)`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Tests/BackoffEnviosTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: politica de reintentos.
// Pura, separada del ejecutor, porque es la regla que mas se va a discutir y la que mas barato es
// cambiar si se testea sola.
using DataPipeline.Core.Envios;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class BackoffEnviosTests
{
    private static readonly DateTime Ahora = new(2026, 9, 12, 3, 4, 0);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void Espacia_los_reintentos_1_2_4_8(int previos, int minutos)
    {
        var (proximo, deshabilitar) = BackoffEnvios.Calcular(previos, Ahora);

        Assert.Equal(Ahora.AddMinutes(minutos), proximo);
        Assert.False(deshabilitar);
    }

    [Fact]
    public void El_quinto_fallo_agota_el_tope_y_deshabilita()
    {
        // MaxIntentos = 5 fallos TOTALES. Con 4 previos, este es el 5o y ultimo: ya no reprograma.
        var (proximo, deshabilitar) = BackoffEnvios.Calcular(Envio.MaxIntentos - 1, Ahora);

        Assert.Null(proximo);
        Assert.True(deshabilitar);
    }

    [Fact]
    public void El_cuarto_fallo_todavia_reprograma()
    {
        Assert.NotNull(BackoffEnvios.Calcular(3, Ahora).ProximoIntento);
        Assert.False(BackoffEnvios.Calcular(3, Ahora).Deshabilitar);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Tests --filter BackoffEnviosTests`
Expected: FAIL de compilación — `BackoffEnvios` no existe.

- [ ] **Step 3: Implementar**

Crear `DataPipeline.Engine/BackoffEnvios.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: politica de reintentos.
// Sin tope, un remoto caido tres dias genera ~4300 intentos y 4300 filas de historial. Con tope, el
// envio se apaga solo y pide intervencion desde el Admin.
using DataPipeline.Core.Envios;

namespace DataPipeline.Engine;

public static class BackoffEnvios
{
    private static readonly int[] Minutos = { 1, 2, 4, 8 };

    /// <summary>
    /// Dado cuantos fallos habia ANTES de este, decide cuando reintentar. Null + true = se agoto el
    /// tope: el envio se deshabilita y no se reintenta solo.
    ///
    /// MaxIntentos son los fallos TOTALES, no los reintentos posteriores al primero: con 4 previos
    /// este es el quinto y ultimo, asi que ya no reprograma. Por eso son cuatro esperas (1, 2, 4 y 8)
    /// y no cinco: despues de la cuarta espera viene el fallo que agota el tope.
    /// </summary>
    public static (DateTime? ProximoIntento, bool Deshabilitar) Calcular(int intentosFallidosPrevios, DateTime ahora)
    {
        if (intentosFallidosPrevios + 1 >= Envio.MaxIntentos) return (null, true);
        var minutos = Minutos[Math.Clamp(intentosFallidosPrevios, 0, Minutos.Length - 1)];
        return (ahora.AddMinutes(minutos), false);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Tests --filter BackoffEnviosTests`
Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Engine/BackoffEnvios.cs tests/DataPipeline.Tests/BackoffEnviosTests.cs
git commit -m "feat(engine): backoff y tope de reintentos de envios (TAREA 00038)"
```

---

### Task 6: `EjecutorEnvios`

**Files:**
- Create: `DataPipeline.Engine/EjecutorEnvios.cs`
- Modify: `DataPipeline.Core/Abstractions/IHistorialCorridas.cs`
- Modify: `DataPipeline.Persistence/EfHistorialCorridas.cs`
- Create: `tests/DataPipeline.Tests/Fakes/FakeDestino.cs`
- Test: `tests/DataPipeline.Tests/EjecutorEnviosTests.cs`

**Interfaces:**
- Consumes: `IEnvioStore` (Task 2), `IDataStore.LeerDesdeAsync` (Task 3), `IDestinoDatos`/`IDestinoFactory`/`ColumnaDestino` (Task 4), `BackoffEnvios.Calcular` (Task 5).
- Produces: `EjecutorEnvios.ProcesarPendientesAsync(DateTime ahora, CancellationToken ct) → Task<int>` (envíos procesados), `IHistorialCorridas.IniciarEnvioAsync(Guid pipelineId, Guid envioId, CancellationToken ct) → Task<Guid>`.

- [ ] **Step 1: Escribir el fake del destino**

Crear `tests/DataPipeline.Tests/Fakes/FakeDestino.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: destino de mentira para
// testear el ejecutor sin un SQL Server.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Security;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests.Fakes;

public sealed class FakeDestino : IDestinoDatos
{
    public List<Record> Escritas { get; } = new();
    public List<ColumnaDestino> ColumnasCreadas { get; } = new();
    public Exception? FallaAlEscribir { get; set; }
    public int VecesEnsure { get; private set; }

    public Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct)
    {
        VecesEnsure++;
        ColumnasCreadas.Clear();
        ColumnasCreadas.AddRange(columnas);
        return Task.CompletedTask;
    }

    public Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct)
    {
        if (FallaAlEscribir is not null) throw FallaAlEscribir;
        Escritas.AddRange(lote.Rows);
        return Task.FromResult((long)lote.Rows.Count);
    }
}

public sealed class FakeDestinoFactory : IDestinoFactory
{
    private readonly IDestinoDatos _destino;
    public FakeDestinoFactory(IDestinoDatos destino) => _destino = destino;
    public IDestinoDatos Crear(DataSource ds, IEncryptionService enc) => _destino;
}
```

- [ ] **Step 2: Escribir el test que falla**

Crear `tests/DataPipeline.Tests/EjecutorEnviosTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de envios.
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Envios;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorEnviosTests
{
    private static readonly DateTime Ahora = new(2026, 9, 12, 3, 4, 0);
    private static readonly string[] Clave = { "id" };

    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejenv_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static RecordBatch Lote(params (int id, int monto)[] filas) => new()
    {
        Columns = new[] { "id", "monto" },
        Rows = filas.Select(f => new Record
        {
            Values = new Dictionary<string, object?> { ["id"] = f.id, ["monto"] = f.monto }
        }).ToList()
    };

    private sealed record Armado(
        EjecutorEnvios Ejecutor, EfEnvioStore Envios, EfHistorialCorridas Historial,
        FakeDestino Destino, Envio Envio);

    private static async Task<Armado> ArmarAsync(bool conDatos = true)
    {
        var meta = NuevoMeta();
        var envios = new EfEnvioStore(meta);
        var pipelines = new EfPipelineStore(meta);
        var historial = new EfHistorialCorridas(meta);
        var origenes = new FakeDataSourceStore();

        var dataCs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejenvd_{Guid.NewGuid():N}.db")};Pooling=False";
        var data = new SqliteDataStore(dataCs);
        await data.EnsureTableAsync("destino", new[] { "id", "monto" }, ModoEscritura.Agregar, CancellationToken.None);
        if (conDatos)
            await data.WriteBatchAsync("destino", Lote((1, 100), (2, 200)), Clave, ModoEscritura.Agregar, CancellationToken.None);

        var pipe = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", TablaDestino = "destino",
            ModoEscritura = ModoEscritura.Agregar, FilasPorLote = 100, ColumnasClave = { "id" }
        };
        await pipelines.UpsertAsync(pipe, CancellationToken.None);

        var origen = new DataSource { Id = Guid.NewGuid(), Nombre = "SQL-CONTA", Tipo = TipoDataSource.SqlServer };
        origenes.Agregar(origen);

        var envio = new Envio
        {
            Id = Guid.NewGuid(), Nombre = "e", Habilitado = true, Pendiente = true,
            PipelineId = pipe.Id, DataSourceId = origen.Id, TablaDestino = "dbo.mw_x"
        };
        await envios.UpsertAsync(envio, CancellationToken.None);

        var destino = new FakeDestino();
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var ejecutor = new EjecutorEnvios(envios, pipelines, origenes, data, historial,
            new FakeDestinoFactory(destino), enc);

        return new Armado(ejecutor, envios, historial, destino, envio);
    }

    [Fact]
    public async Task Empuja_las_filas_y_apaga_la_bandera()
    {
        var a = await ArmarAsync();

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(2, a.Destino.Escritas.Count);
        var e = (await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!;
        Assert.False(e.Pendiente);
        Assert.Null(e.InicioEnCurso);
        Assert.NotNull(e.UltimoCreado);
        Assert.Equal(EstadoEnvio.AlDia, e.Estado);
    }

    [Fact]
    public async Task No_manda_la_columna_procesado_al_destino()
    {
        var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.DoesNotContain(a.Destino.ColumnasCreadas, c => c.Nombre == ServingColumns.Procesado);
        Assert.All(a.Destino.Escritas, f => Assert.False(f.Values.ContainsKey(ServingColumns.Procesado)));
    }

    [Fact]
    public async Task Infiere_el_tipo_de_las_columnas_desde_el_primer_lote()
    {
        var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var id = a.Destino.ColumnasCreadas.Single(c => c.Nombre == "id");
        Assert.Equal(typeof(long), id.TipoClr);
    }

    [Fact]
    public async Task Sin_filas_nuevas_apaga_la_bandera_y_no_toca_el_destino()
    {
        var a = await ArmarAsync(conDatos: false);

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Empty(a.Destino.Escritas);
        Assert.Equal(0, a.Destino.VecesEnsure);
        Assert.False((await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!.Pendiente);
    }

    [Fact]
    public async Task La_segunda_corrida_no_reenvia_lo_que_ya_mando()
    {
        var a = await ArmarAsync();
        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);
        a.Destino.Escritas.Clear();

        await a.Envios.MarcarPendientesAsync(
            (await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!.PipelineId, CancellationToken.None);
        await a.Ejecutor.ProcesarPendientesAsync(Ahora.AddMinutes(1), CancellationToken.None);

        Assert.Empty(a.Destino.Escritas);
    }

    [Fact]
    public async Task Si_el_destino_falla_suma_intento_y_deja_pendiente()
    {
        var a = await ArmarAsync();
        a.Destino.FallaAlEscribir = new InvalidOperationException("Login failed for user 'mw_push'.");

        var n = await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        Assert.Equal(1, n);
        var e = (await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!;
        Assert.Equal(1, e.IntentosFallidos);
        Assert.True(e.Pendiente);
        Assert.Null(e.InicioEnCurso);
        Assert.Null(e.UltimoCreado);
        Assert.Equal(Ahora.AddMinutes(1), e.ProximoIntento);
        Assert.Contains("Login failed", e.UltimoError);
    }

    [Fact]
    public async Task Al_agotar_los_reintentos_se_deshabilita()
    {
        var a = await ArmarAsync();
        a.Destino.FallaAlEscribir = new InvalidOperationException("sigue caido");
        var envio = (await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!;
        envio.IntentosFallidos = Envio.MaxIntentos - 1;
        envio.ProximoIntento = null;
        await a.Envios.UpsertAsync(envio, CancellationToken.None);

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var e = (await a.Envios.GetByIdAsync(a.Envio.Id, CancellationToken.None))!;
        Assert.False(e.Habilitado);
        Assert.Equal(Envio.MaxIntentos, e.IntentosFallidos);
        Assert.Equal(EstadoEnvio.Fallido, e.Estado);
    }

    [Fact]
    public async Task Registra_la_corrida_en_el_historial_con_el_EnvioId()
    {
        var a = await ArmarAsync();

        await a.Ejecutor.ProcesarPendientesAsync(Ahora, CancellationToken.None);

        var corridas = await a.Historial.ObtenerRecientesAsync(10, CancellationToken.None);
        var deEnvio = Assert.Single(corridas, c => c.EnvioId == a.Envio.Id);
        Assert.Equal(EstadoCorrida.Ok, deEnvio.Estado);
        Assert.Equal(2, deEnvio.FilasEscritas);
    }
}
```

> **`FakeDataSourceStore` necesita un método `Agregar`.** Abrir `tests/DataPipeline.Tests/Fakes/FakeDataSourceStore.cs` y, si no lo tiene, agregarlo con el comentario de versionado (`Linea: 2`), sin romper su uso actual en `PipelineHydratorTests`.

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Tests --filter EjecutorEnviosTests`
Expected: FAIL de compilación — `EjecutorEnvios` no existe.

- [ ] **Step 4: Extender el historial**

En `DataPipeline.Core/Abstractions/IHistorialCorridas.cs`, agregar en la interfaz:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: una sola tabla de
    // historial para corridas de pipeline y de envio, distinguidas por EnvioId.
    Task<Guid> IniciarEnvioAsync(Guid pipelineId, Guid envioId, CancellationToken ct);
```

En `DataPipeline.Persistence/EfHistorialCorridas.cs`, agregar después de `IniciarAsync`:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push.
    public async Task<Guid> IniciarEnvioAsync(Guid pipelineId, Guid envioId, CancellationToken ct)
    {
        var run = new PipelineRun
        {
            Id = Guid.NewGuid(), PipelineId = pipelineId, EnvioId = envioId,
            Inicio = DateTime.Now, Estado = EstadoCorrida.EnCurso
        };
        _db.Corridas.Add(run);
        await _db.SaveChangesAsync(ct);
        return run.Id;
    }
```

- [ ] **Step 5: Implementar el ejecutor**

Crear `DataPipeline.Engine/EjecutorEnvios.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de envios.
// El pipeline no ejecuta el envio: solo prende Pendiente. Quien ejecuta es siempre este barrido,
// llamado desde el mismo tick de 60s que ya corre los pipelines. Por eso el disparo y el reintento
// son la misma operacion, y no dos caminos que haya que mantener sincronizados.
//
// El cursor (UltimoCreado) avanza SOLO al final del envio completo, nunca por lote: si se cortara a
// la mitad, las filas del ultimo lote quedarian marcadas como enviadas sin haber llegado.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using DataPipeline.Core.Records;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

public sealed class EjecutorEnvios
{
    private const int FilasPorLote = 5_000;

    private readonly IEnvioStore _envios;
    private readonly IPipelineStore _pipelines;
    private readonly IDataSourceStore _origenes;
    private readonly IDataStore _data;
    private readonly IHistorialCorridas _historial;
    private readonly IDestinoFactory _destinos;
    private readonly IEncryptionService _encryption;

    public EjecutorEnvios(IEnvioStore envios, IPipelineStore pipelines, IDataSourceStore origenes,
        IDataStore data, IHistorialCorridas historial, IDestinoFactory destinos, IEncryptionService encryption)
    {
        _envios = envios;
        _pipelines = pipelines;
        _origenes = origenes;
        _data = data;
        _historial = historial;
        _destinos = destinos;
        _encryption = encryption;
    }

    /// <summary>Procesa los envios pendientes. Devuelve cuantos tomo (con exito o no).</summary>
    public async Task<int> ProcesarPendientesAsync(DateTime ahora, CancellationToken ct)
    {
        var pendientes = await _envios.TomarPendientesAsync(ahora, ct);
        var procesados = 0;

        foreach (var envio in pendientes)
        {
            try
            {
                await ProcesarUnoAsync(envio, ahora, ct);
            }
            catch (Exception ex)
            {
                var (proximo, deshabilitar) = BackoffEnvios.Calcular(envio.IntentosFallidos, ahora);
                await _envios.RegistrarFalloAsync(envio.Id, ex.Message, proximo, deshabilitar, ct);
            }
            procesados++;
        }

        return procesados;
    }

    private async Task ProcesarUnoAsync(Envio envio, DateTime ahora, CancellationToken ct)
    {
        var pipeline = await _pipelines.GetByIdAsync(envio.PipelineId, ct)
            ?? throw new InvalidOperationException($"El envio '{envio.Nombre}' apunta a un pipeline que no existe.");
        var origen = await _origenes.GetByIdAsync(envio.DataSourceId, ct)
            ?? throw new InvalidOperationException($"El envio '{envio.Nombre}' apunta a un origen que no existe.");

        // Se lee la primera pagina antes de abrir la conexion remota: sin filas nuevas no hay para
        // que molestar al servidor destino.
        var primera = await _data.LeerDesdeAsync(pipeline.TablaDestino, envio.UltimoCreado, 1, FilasPorLote, ct);
        if (primera.Rows.Count == 0)
        {
            await _envios.RegistrarExitoAsync(envio.Id, envio.UltimoCreado ?? DateTime.MinValue, ahora, ct);
            return;
        }

        var runId = await _historial.IniciarEnvioAsync(envio.PipelineId, envio.Id, ct);
        try
        {
            var destino = _destinos.Crear(origen, _encryption);
            await destino.EnsureTableAsync(envio.TablaDestino, ColumnasDe(primera), ct);

            long escritas = 0;
            var maximo = envio.UltimoCreado ?? DateTime.MinValue;
            var pagina = 1;
            var lote = primera;

            while (lote.Rows.Count > 0)
            {
                escritas += await destino.WriteBatchAsync(envio.TablaDestino, SinProcesado(lote), pipeline.ColumnasClave, ct);
                maximo = Maximo(maximo, lote);
                if (lote.Rows.Count < FilasPorLote) break;
                pagina++;
                lote = await _data.LeerDesdeAsync(pipeline.TablaDestino, envio.UltimoCreado, pagina, FilasPorLote, ct);
            }

            await _historial.CompletarAsync(runId, true, escritas, escritas, null, ct);
            await _envios.RegistrarExitoAsync(envio.Id, maximo, ahora, ct);
        }
        catch (Exception ex)
        {
            await _historial.CompletarAsync(runId, false, 0, 0,
                $"Intento {envio.IntentosFallidos + 1}/{Envio.MaxIntentos}. {ex.Message}", ct);
            throw;
        }
    }

    /// <summary>
    /// Tipo de cada columna tomado del primer valor no nulo del primer lote (decision D2 del plan).
    /// _procesado se descarta: es el ack de nuestros consumidores PULL, del otro lado no significa nada.
    /// </summary>
    private static List<ColumnaDestino> ColumnasDe(RecordBatch lote) =>
        lote.Columns
            .Where(c => c != ServingColumns.Procesado && c != ServingColumns.Key && c != ServingColumns.Creado)
            .Select(c => new ColumnaDestino(c,
                lote.Rows.Select(f => f.Values.TryGetValue(c, out var v) ? v : null)
                    .FirstOrDefault(v => v is not null)?.GetType()))
            .ToList();

    private static RecordBatch SinProcesado(RecordBatch lote) => new()
    {
        Columns = lote.Columns.Where(c => c != ServingColumns.Procesado).ToArray(),
        Rows = lote.Rows.Select(f => new Core.Records.Record
        {
            Values = f.Values.Where(kv => kv.Key != ServingColumns.Procesado)
                             .ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList()
    };

    private static DateTime Maximo(DateTime actual, RecordBatch lote)
    {
        foreach (var fila in lote.Rows)
        {
            if (!fila.Values.TryGetValue(ServingColumns.Creado, out var v) || v is null) continue;
            var creado = Convert.ToDateTime(v);
            if (creado > actual) actual = creado;
        }
        return actual;
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Tests --filter EjecutorEnviosTests`
Expected: PASS — 8 tests.

- [ ] **Step 7: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 156 verdes.

- [ ] **Step 8: Commit**

```bash
git add DataPipeline.Engine/EjecutorEnvios.cs DataPipeline.Core/Abstractions/IHistorialCorridas.cs DataPipeline.Persistence/EfHistorialCorridas.cs tests/DataPipeline.Tests/Fakes/ tests/DataPipeline.Tests/EjecutorEnviosTests.cs
git commit -m "feat(engine): EjecutorEnvios, barrido de pendientes con cursor y reintentos (TAREA 00038)"
```

---

### Task 7: Enganche en el scheduler

**Files:**
- Modify: `DataPipeline.Engine/EjecutorProgramado.cs`
- Modify: `DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs`
- Modify: `DataPipeline.Api/Controllers/RunsController.cs`
- Modify: `DataPipeline.Api/Program.cs`
- Modify: `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`
- Test: `tests/DataPipeline.Tests/EjecutorProgramadoEnviosTests.cs`

**Interfaces:**
- Consumes: `IEnvioStore.MarcarPendientesAsync` (Task 2), `EjecutorEnvios.ProcesarPendientesAsync` (Task 6).
- Produces: `EjecutorProgramado` pasa a recibir `IEnvioStore` como **sexto** parámetro del constructor: `(IPipelineStore, IHistorialCorridas, PipelineRunner, IEncryptionService, PipelineHydrator, IEnvioStore)`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Tests/EjecutorProgramadoEnviosTests.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el pipeline solo prende la
// bandera. Que la corrida fallida NO la prenda es la garantia de que nunca se empuja una tanda a
// medias.
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Envios;
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using DataPipeline.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class EjecutorProgramadoEnviosTests
{
    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"ejpenv_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static string NuevaData() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"ejpenvd_{Guid.NewGuid():N}.db")};Pooling=False";

    private static async Task<(EjecutorProgramado Ej, EfEnvioStore Envios, Envio Envio)> ArmarAsync(bool origenFalla)
    {
        var meta = NuevoMeta();
        var store = new EfPipelineStore(meta);
        var hist = new EfHistorialCorridas(meta);
        var envios = new EfEnvioStore(meta);
        var ahora = new DateTime(2026, 9, 12, 3, 0, 0);

        var def = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = "p", Habilitado = true,
            Programacion = TipoProgramacion.Diaria, HoraDelDia = "03:00",
            ProximaCorrida = ahora.AddMinutes(-1),
            TablaDestino = "destino", ModoEscritura = ModoEscritura.Reemplazar,
            FilasPorLote = 10, ColumnasClave = { "id" },
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "Fake" } }
        };
        await store.UpsertAsync(def, CancellationToken.None);

        var envio = new Envio
        {
            Id = Guid.NewGuid(), Nombre = "e", Habilitado = true, Pendiente = false,
            PipelineId = def.Id, DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
        };
        await envios.UpsertAsync(envio, CancellationToken.None);

        var source = origenFalla
            ? new FakeSource(new[] { "id" }, new[] { new Record { Values = new() { ["id"] = 1 } } }, 10)
              { FallaAlLeer = new InvalidOperationException("origen caido") }
            : new FakeSource(new[] { "id" }, new[] { new Record { Values = new() { ["id"] = 1 } } }, 10);

        var runner = new PipelineRunner(new FakeSourceFactory(source), new TransformFactory(), new SqliteDataStore(NuevaData()));
        var enc = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), enc);
        var ej = new EjecutorProgramado(store, hist, runner, enc, hydrator, envios);

        return (ej, envios, envio);
    }

    [Fact]
    public async Task Una_corrida_exitosa_prende_la_bandera_de_sus_envios()
    {
        var (ej, envios, envio) = await ArmarAsync(origenFalla: false);

        await ej.ProcesarVencidosAsync(new DateTime(2026, 9, 12, 3, 0, 0), CancellationToken.None);

        Assert.True((await envios.GetByIdAsync(envio.Id, CancellationToken.None))!.Pendiente);
    }

    [Fact]
    public async Task Una_corrida_fallida_no_prende_nada()
    {
        var (ej, envios, envio) = await ArmarAsync(origenFalla: true);

        await ej.ProcesarVencidosAsync(new DateTime(2026, 9, 12, 3, 0, 0), CancellationToken.None);

        Assert.False((await envios.GetByIdAsync(envio.Id, CancellationToken.None))!.Pendiente);
    }
}
```

> **`FakeSource` necesita `FallaAlLeer`.** Abrir `tests/DataPipeline.Tests/Fakes/FakeSource.cs` y agregar la propiedad `public Exception? FallaAlLeer { get; set; }`, lanzada al principio del `ReadAsync`, con el comentario de versionado (`Linea: 2`). No cambiar el comportamiento por defecto (null = no falla), para no tocar los tests existentes.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Tests --filter EjecutorProgramadoEnviosTests`
Expected: FAIL de compilación — `EjecutorProgramado` no tiene un constructor de 6 parámetros.

- [ ] **Step 3: Prender la bandera en `EjecutorProgramado`**

En `DataPipeline.Engine/EjecutorProgramado.cs`:

Agregar el campo y el parámetro del constructor:

```csharp
    // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push.
    private readonly IEnvioStore _envios;
```

El constructor pasa a ser:

```csharp
    public EjecutorProgramado(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption, PipelineHydrator hydrator,
        IEnvioStore envios)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
        _hydrator = hydrator;
        _envios = envios;
    }
```

Dentro del `try` de `ProcesarVencidosAsync`, **inmediatamente después** de la línea `await _store.ActualizarWatermarkAsync(...)`:

```csharp
                // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el pipeline
                // NO ejecuta el envio, solo avisa que hay algo. Quien ejecuta es el mismo tick, via
                // EjecutorEnvios. Se prende solo si la corrida salio bien: una tanda a medias no se
                // empuja nunca.
                if (result.Success)
                    await _envios.MarcarPendientesAsync(def.Id, ct);
```

- [ ] **Step 4: Arreglar los tests existentes que construyen `EjecutorProgramado`**

En `tests/DataPipeline.Tests/EjecutorProgramadoTests.cs`, cada `new EjecutorProgramado(store, hist, runner, enc, hydrator)` pasa a `new EjecutorProgramado(store, hist, runner, enc, hydrator, new EfEnvioStore(meta))`, con el `using DataPipeline.Persistence;` ya presente.

Run: `grep -n "new EjecutorProgramado" tests/DataPipeline.Tests/EjecutorProgramadoTests.cs` para ubicar todas las ocurrencias.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Tests --filter EjecutorProgramado`
Expected: PASS — los 2 nuevos + los existentes de `EjecutorProgramadoTests`.

- [ ] **Step 6: Correr los envíos en el tick**

En `DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs`, dentro del `try` del `while`, reemplazar el bloque del ejecutor por:

```csharp
                using var scope = _services.CreateScope();
                var ahora = DateTime.Now;

                var ejecutor = scope.ServiceProvider.GetRequiredService<EjecutorProgramado>();
                var n = await ejecutor.ProcesarVencidosAsync(ahora, stoppingToken);
                if (n > 0) _logger.LogInformation("PipelineScheduler: {N} pipeline(s) procesado(s).", n);

                // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de
                // envios va DESPUES de los pipelines en el mismo tick, asi lo que acaba de aterrizar
                // sale en la misma pasada en vez de esperar 60s. Se pasa el mismo 'ahora' que los
                // pipelines para que el backoff sea determinista.
                var envios = scope.ServiceProvider.GetRequiredService<EjecutorEnvios>();
                var e = await envios.ProcesarPendientesAsync(ahora, stoppingToken);
                if (e > 0) _logger.LogInformation("PipelineScheduler: {N} envio(s) procesado(s).", e);
```

- [ ] **Step 7: Prender la bandera también en la corrida manual**

En `DataPipeline.Api/Controllers/RunsController.cs`, agregar `IEnvioStore _envios` al constructor (mismo patrón que los demás campos) y, en `Run`, después del `ActualizarWatermarkAsync`:

```csharp
        // Fecha: 12/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: una corrida manual
        // dispara los envios igual que una programada. El barrido del tick los levanta.
        if (result.Success)
            await _envios.MarcarPendientesAsync(def.Id, ct);
```

- [ ] **Step 8: Registrar todo en DI**

En `DataPipeline.Api/Program.cs`, junto a los demás `AddScoped`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
builder.Services.AddScoped<IEnvioStore, EfEnvioStore>();
builder.Services.AddScoped<IDestinoFactory, DataPipeline.Connectors.Destinos.DestinoFactory>();
builder.Services.AddScoped<EjecutorEnvios>();
```

- [ ] **Step 9: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 158 verdes.

- [ ] **Step 10: Commit**

```bash
git add DataPipeline.Engine/EjecutorProgramado.cs DataPipeline.Api/Hosting/PipelineSchedulerHostedService.cs DataPipeline.Api/Controllers/RunsController.cs DataPipeline.Api/Program.cs tests/DataPipeline.Tests/
git commit -m "feat(engine): el pipeline prende Pendiente y el tick barre los envios (TAREA 00038)"
```

---

### Task 8: API de envíos

**Files:**
- Create: `DataPipeline.Api/Controllers/EnviosController.cs`
- Test: `tests/DataPipeline.Api.Tests/EnviosApiTests.cs`

**Interfaces:**
- Consumes: `IEnvioStore` (Task 2), DI de Task 7.
- Produces: `GET /api/envios`, `GET /api/envios/{id}`, `POST /api/envios`, `PUT /api/envios/{id}`, `DELETE /api/envios/{id}`, `POST /api/envios/{id}/reintentar`. Todos con el `BasicAuthHandler.Scheme` como el resto.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/DataPipeline.Api.Tests/EnviosApiTests.cs`, copiando el patrón de autenticación y de `ApiFactory` de `tests/DataPipeline.Api.Tests/GestionApiTests.cs` (leerlo primero: define cómo se arma el `HttpClient` autenticado):

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: CRUD y reintento.
using System.Net;
using System.Net.Http.Json;
using DataPipeline.Core.Envios;
using Xunit;

namespace DataPipeline.Api.Tests;

public class EnviosApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public EnviosApiTests(ApiFactory factory) => _factory = factory;

    private static Envio Nuevo(string nombre) => new()
    {
        Id = Guid.NewGuid(), Nombre = nombre, Habilitado = true,
        PipelineId = Guid.NewGuid(), DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
    };

    [Fact]
    public async Task Sin_auth_devuelve_401()
    {
        var anonimo = _factory.CreateClient();

        var r = await anonimo.GetAsync("api/envios");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Crea_lista_y_borra_un_envio()
    {
        var client = _factory.ClienteAdmin();
        var envio = Nuevo($"e_{Guid.NewGuid():N}");

        var creado = await client.PostAsJsonAsync("api/envios", envio);
        Assert.Equal(HttpStatusCode.OK, creado.StatusCode);

        var lista = await client.GetFromJsonAsync<List<Envio>>("api/envios");
        Assert.Contains(lista!, e => e.Id == envio.Id);

        var borrado = await client.DeleteAsync($"api/envios/{envio.Id}");
        Assert.Equal(HttpStatusCode.OK, borrado.StatusCode);

        var despues = await client.GetAsync($"api/envios/{envio.Id}");
        Assert.Equal(HttpStatusCode.NotFound, despues.StatusCode);
    }

    [Fact]
    public async Task Rechaza_un_envio_sin_tabla_destino()
    {
        var client = _factory.ClienteAdmin();
        var envio = Nuevo($"e_{Guid.NewGuid():N}");
        envio.TablaDestino = "";

        var r = await client.PostAsJsonAsync("api/envios", envio);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Reintentar_reactiva_un_envio_fallido()
    {
        var client = _factory.ClienteAdmin();
        var envio = Nuevo($"e_{Guid.NewGuid():N}");
        envio.Habilitado = false;
        envio.IntentosFallidos = Envio.MaxIntentos;
        await client.PostAsJsonAsync("api/envios", envio);

        var r = await client.PostAsync($"api/envios/{envio.Id}/reintentar", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var recargado = await client.GetFromJsonAsync<Envio>($"api/envios/{envio.Id}");
        Assert.True(recargado!.Habilitado);
        Assert.Equal(0, recargado.IntentosFallidos);
        Assert.True(recargado.Pendiente);
    }

    [Fact]
    public async Task Reintentar_un_envio_inexistente_devuelve_404()
    {
        var client = _factory.ClienteAdmin();

        var r = await client.PostAsync($"api/envios/{Guid.NewGuid()}/reintentar", null);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
```

> Si `ApiFactory` no expone un helper `ClienteAdmin()`, usar el mismo mecanismo que `GestionApiTests` para autenticar y, si conviene, **agregar** el helper a `ApiFactory` con el comentario de versionado (`Linea: 1`).

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter EnviosApiTests`
Expected: FAIL — 404 en `api/envios` (el controller no existe).

- [ ] **Step 3: Implementar el controller**

Crear `DataPipeline.Api/Controllers/EnviosController.cs`:

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: CRUD + reintento manual.
// Mismo molde que DataSourcesController: Basic Auth, rol admin, sin logica de negocio.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme)]
[Route("api/envios")]
public sealed class EnviosController : ControllerBase
{
    private readonly IEnvioStore _envios;
    public EnviosController(IEnvioStore envios) => _envios = envios;

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _envios.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        var envio = await _envios.GetByIdAsync(id, ct);
        return envio is null ? NotFound($"Envio {id} no encontrado.") : Ok(envio);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] Envio envio, CancellationToken ct) => await GuardarAsync(envio, ct);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] Envio envio, CancellationToken ct)
    {
        if (envio.Id != id) return BadRequest("El id del cuerpo no coincide con el de la ruta.");
        return await GuardarAsync(envio, ct);
    }

    private async Task<IActionResult> GuardarAsync(Envio envio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envio.Nombre)) return BadRequest("El nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(envio.TablaDestino)) return BadRequest("La tabla destino es obligatoria.");
        if (envio.PipelineId == Guid.Empty) return BadRequest("Hay que elegir un pipeline.");
        if (envio.DataSourceId == Guid.Empty) return BadRequest("Hay que elegir una conexion destino.");

        if (envio.Id == Guid.Empty) envio.Id = Guid.NewGuid();
        await _envios.UpsertAsync(envio, ct);
        return Ok(envio);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Borrar(Guid id, CancellationToken ct) =>
        await _envios.DeleteAsync(id, ct) ? Ok() : NotFound($"Envio {id} no encontrado.");

    // Reactiva un envio que agoto los reintentos y se deshabilito solo. Lo deja Pendiente: el
    // barrido del proximo tick lo levanta, no hace falta esperar a que corra el pipeline.
    [HttpPost("{id:guid}/reintentar")]
    public async Task<IActionResult> Reintentar(Guid id, CancellationToken ct)
    {
        if (await _envios.GetByIdAsync(id, ct) is null) return NotFound($"Envio {id} no encontrado.");
        await _envios.ReactivarAsync(id, ct);
        return Ok();
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter EnviosApiTests`
Expected: PASS — 5 tests.

- [ ] **Step 5: Build limpio y suite completa**

Run: `dotnet build --no-incremental` → 0 warnings.
Run: `dotnet test` → 163 verdes.

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Controllers/EnviosController.cs tests/DataPipeline.Api.Tests/EnviosApiTests.cs
git commit -m "feat(api): CRUD de envios + reintento manual (TAREA 00038)"
```

---

### Task 9: Admin — lista de envíos

**Files:**
- Create: `DataPipeline.Admin/Services/EnviosAdminService.cs`
- Create: `DataPipeline.Admin/Components/Pages/Envios/Index.razor`
- Modify: `DataPipeline.Admin/Components/Layout/NavMenu.razor`
- Modify: `DataPipeline.Admin/Program.cs` (registro del `HttpClient` del servicio)

**Interfaces:**
- Consumes: `GET /api/envios`, `POST /api/envios/{id}/reintentar` (Task 8).
- Produces: `EnviosAdminService` con `ObtenerTodosAsync()`, `ObtenerAsync(Guid)`, `GuardarAsync(Envio)`, `BorrarAsync(Guid)`, `ReintentarAsync(Guid)`.

**Maquetas:** `docs/Diseño/00038-envio-sql/01-envios-lista.png` (con datos) y `02-envios-vacio.png` (estado vacío). **Construir lo que está ahí, ni más ni menos.**

> **Sin tests automatizados:** el Admin es build-only por convención del repo. La verificación es `dotnet build --no-incremental` limpio + revisión visual contra la maqueta.

- [ ] **Step 1: Crear el servicio cliente**

Crear `DataPipeline.Admin/Services/EnviosAdminService.cs`, calcando `DataSourcesAdminService.cs` (leerlo primero para copiar el manejo de `HttpClient` y `JsonSerializerOptions`):

```csharp
// Fecha: 12/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo: el Admin no toca la BD, habla
// con la API por HttpClient. Mismo molde que DataSourcesAdminService.
using System.Text.Json;
using DataPipeline.Core.Envios;

namespace DataPipeline.Admin.Services;

public class EnviosAdminService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public EnviosAdminService(HttpClient http) => _http = http;

    public async Task<List<Envio>> ObtenerTodosAsync()
    {
        var r = await _http.GetAsync("api/envios");
        if (!r.IsSuccessStatusCode) return new();
        return JsonSerializer.Deserialize<List<Envio>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }

    public async Task<Envio?> ObtenerAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/envios/{id}");
        if (!r.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<Envio>(await r.Content.ReadAsStringAsync(), _json);
    }

    public async Task<(bool Ok, string? Error)> GuardarAsync(Envio envio)
    {
        var r = await _http.PostAsJsonAsync("api/envios", envio);
        return r.IsSuccessStatusCode ? (true, null) : (false, await r.Content.ReadAsStringAsync());
    }

    public async Task<bool> BorrarAsync(Guid id) =>
        (await _http.DeleteAsync($"api/envios/{id}")).IsSuccessStatusCode;

    public async Task<bool> ReintentarAsync(Guid id) =>
        (await _http.PostAsync($"api/envios/{id}/reintentar", null)).IsSuccessStatusCode;
}
```

En `DataPipeline.Admin/Program.cs`, registrar el servicio **con la misma configuración de `HttpClient`** que usa `DataSourcesAdminService` (copiar esa línea, cambiando el tipo).

- [ ] **Step 2: Crear la página de lista**

Crear `DataPipeline.Admin/Components/Pages/Envios/Index.razor`. Contenido requerido, contra `01-envios-lista.png` y `02-envios-vacio.png`:

- `@page "/envios"`, `<PageTitle>Envios — Admin</PageTitle>`.
- `page-header` con título **Envios**, subtítulo *"Push del resultado de un pipeline a una tabla en una base externa."* y botón primario **+ Nuevo envio** que navega a `/envios/editar/{Guid.NewGuid()}`.
- `SfGrid` con `AllowPaging` y `AllowSorting`, columnas: **Nombre**, **Pipeline**, **Destino** (conexión + tabla debajo, en `mono`), **Estado**, **Ultimo envio**, **Filas**, **Ultimo error**, y una de acciones.
- Columna **Estado** como `<span class="e-badge @badge">`, con el mismo `switch` que usa `Runs/Index.razor`:

```csharp
    private static string BadgeDe(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.AlDia => "e-badge-success",
        EstadoEnvio.Pendiente => "e-badge-warning",
        EstadoEnvio.Reintentando => "e-badge-info",
        EstadoEnvio.Fallido => "e-badge-danger",
        _ => "e-badge-secondary"
    };
```

- Texto del badge: `Reintentando` muestra `$"Reintentando {envio.IntentosFallidos}/{Envio.MaxIntentos}"`; el resto, el nombre del estado.
- Acciones: **Editar** en todas las filas; **Reintentar** (en rojo) **sólo** si `envio.Estado == EstadoEnvio.Fallido`, que llama a `ReintentarAsync` y recarga la grilla.
- Estado vacío: si la lista está vacía, en vez de la grilla va el panel de `02-envios-vacio.png` — ícono, **"Todavia no hay envios"**, el párrafo explicativo, y el botón **+ Crear el primero**.
- Al pie, el `note` de la maqueta: *"Los envios no tienen horario propio. Cada uno corre cuando su pipeline termina bien, y el scheduler lo reintenta solo si falla. Para cambiar la frecuencia, se cambia la del pipeline."*
- El nombre del pipeline y de la conexión se resuelven pidiendo también `PipelinesAdminService.ObtenerTodosAsync()` y `DataSourcesAdminService.ObtenerTodosAsync()` en `OnInitializedAsync`, y armando dos diccionarios `Guid → nombre`.
- Comentario de versionado arriba de todo: `@* Fecha: 12/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo: lista de envios. *@`

- [ ] **Step 3: Agregar la entrada al menú**

En `DataPipeline.Admin/Components/Layout/NavMenu.razor`, **entre** el `NavLink` de `pipelines` y el de `runs`:

```razor
        @* Fecha: 12/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo. *@
        <NavLink class="nav-link" href="envios">
            <span class="e-icons e-export" aria-hidden="true"></span> Envios
        </NavLink>
```

- [ ] **Step 4: Verificar**

Run: `dotnet build --no-incremental` → 0 warnings.
Levantar el Admin y abrir `/envios`. Comparar contra `01-envios-lista.png` y `02-envios-vacio.png`: los cinco badges, el botón Reintentar sólo en los fallidos, el estado vacío, el aviso del pie.

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Admin/Services/EnviosAdminService.cs DataPipeline.Admin/Components/Pages/Envios/Index.razor DataPipeline.Admin/Components/Layout/NavMenu.razor DataPipeline.Admin/Program.cs
git commit -m "feat(admin): lista de envios con los cinco estados (TAREA 00038)"
```

---

### Task 10: Admin — alta y edición

**Files:**
- Create: `DataPipeline.Admin/Components/Pages/Envios/Form.razor`

**Interfaces:**
- Consumes: `EnviosAdminService` (Task 9), `PipelinesAdminService`, `DataSourcesAdminService`, el endpoint de probar conexión que ya usa `DataSources/Index.razor`.

**Maquetas:** `03-envio-form.png` (normal) y `04-envio-form-error.png` (error).

- [ ] **Step 1: Crear el formulario**

Crear `DataPipeline.Admin/Components/Pages/Envios/Form.razor`, con `@page "/envios/editar/{Id:guid}"`. Contra las maquetas:

**Columna izquierda — panel "Datos del envio":**

| Campo | Control | Detalle |
|---|---|---|
| Nombre | `SfTextBox` | Obligatorio |
| Pipeline | `SfDropDownList<Guid, PipelineDefinition>` | Hint: *"El envio corre cuando este pipeline termina bien. **No tiene horario propio:** hereda la cadencia del pipeline (hoy: `{Programacion} {HoraDelDia}`)."* El tag con la cadencia se arma del pipeline elegido y **se actualiza al cambiarlo** |
| Conexión destino | `SfDropDownList<Guid, DataSource>` **filtrado a `Tipo == SqlServer`** + botón **Probar conexion** | El resultado va en un `alert-ok`/`alert-err` debajo, con el mensaje **real** del servidor (ver `04`) |
| Tabla destino | `SfTextBox` | Obligatorio. Hint: *"Se crea automaticamente en el servidor remoto si no existe. Requiere permiso de `CREATE TABLE` en esa base."* |
| Habilitado | `SfSwitch` | Hint: *"Deshabilitado, el pipeline sigue corriendo pero no se empuja nada."* |

Botones **Guardar** (deshabilitado si hay error de validación) y **Cancelar** (vuelve a `/envios`).

**Columna derecha:**

1. El aviso fijo, en `note`: **"Este envio mueve columnas, no transforma."** + *"Lo que hay en la tabla intermedia es exactamente lo que llega al destino. Para renombrar, castear o filtrar, se usan las transformaciones del pipeline."*
2. Panel **Estado**, de sólo lectura: Estado (badge), Último envío, Filas empujadas, **Cursor (`_creado` hasta)** = `UltimoCreado`, Intentos fallidos (`{IntentosFallidos} / {Envio.MaxIntentos}`).
3. Hint: *"El cursor avanza solo cuando el envio termina bien. Una fila modificada por el pipeline vuelve a viajar sola."*
4. Botón de ancho completo: **Forzar envio ahora** en estado normal; **Reintentar ahora** (primario) cuando `Estado == Fallido`. Los dos llaman a `ReintentarAsync`.

**Estado de error (`04-envio-form-error.png`):**
- Falla de "probar conexión" → `alert-err` con **"No se pudo conectar."** y el mensaje textual de SQL Server.
- Tabla destino vacía → borde rojo + *"El nombre de la tabla destino es obligatorio."*
- Si `Estado == Fallido`, arriba del panel Estado va el `alert-err`: **"Envio fallido."** + *"Se agotaron los 5 reintentos (1, 2, 4 y 8 min). No se vuelve a intentar solo hasta que alguien lo reactive desde aca."*
- En ese estado el panel muestra además **Filas esperando** en ámbar, y el hint: *"Las filas no se pierden: quedan en la tabla intermedia y **siguen disponibles por GET /api/data**. El envio las va a empujar cuando vuelva a funcionar."*

Comentario de versionado arriba de todo: `@* Fecha: 12/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo: alta/edicion. *@`

> **"Filas esperando" no lo devuelve ningún endpoint de hoy.** Mostrar `—` cuando no se pueda calcular y **no** inventar un endpoint nuevo: queda anotado como pendiente en el Historial. Es preferible a agregar superficie que el diseño no acordó.

- [ ] **Step 2: Verificar**

Run: `dotnet build --no-incremental` → 0 warnings.
Abrir `/envios/editar/{id}` de un envío sano y de uno fallido. Comparar contra `03-envio-form.png` y `04-envio-form-error.png`.

- [ ] **Step 3: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Envios/Form.razor
git commit -m "feat(admin): alta/edicion de envios con probar conexion (TAREA 00038)"
```

---

### Task 11: Admin — envíos en el historial de corridas

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Runs/Index.razor`

**Maqueta:** `05-corridas.png`.

- [ ] **Step 1: Agregar la columna Tipo y los filtros**

En `DataPipeline.Admin/Components/Pages/Runs/Index.razor`:

1. Subtítulo: *"Historial de ejecuciones de los pipelines y sus envios."*
2. En el `page-header`, tres botones de filtro: **Todas** / **Solo pipelines** / **Solo envios**, que filtran en memoria por `r.EnvioId is null`.
3. Columna **Tipo** nueva, después de Fin:

```razor
        @* Fecha: 12/09/2026 - TAREA: 00038 - Linea: 3 - Envio a SQL externo: una sola tabla de
           historial; EnvioId distingue la corrida de envio de la de pipeline. *@
        <GridColumn HeaderText="Tipo" Width="110" TextAlign="TextAlign.Center">
            <Template Context="ctx">
                @{ var r = (PipelineRun)ctx; }
                <span class="e-badge @(r.EnvioId is null ? "e-badge-primary" : "e-badge-info")">
                    @(r.EnvioId is null ? "Pipeline" : "Envio")
                </span>
            </Template>
        </GridColumn>
```

4. Columna **Nombre**: el del pipeline, o el del envío cuando `EnvioId` tiene valor. Resolver con dos diccionarios `Guid → nombre` armados en `OnInitializedAsync` desde `PipelinesAdminService` y `EnviosAdminService`.
5. Inyectar `EnviosAdminService` y `PipelinesAdminService` en la página.
6. Al pie, el `note` de la maqueta sobre la tabla única de historial.

- [ ] **Step 2: Verificar**

Run: `dotnet build --no-incremental` → 0 warnings.
Abrir `/runs` con al menos una corrida de pipeline y una de envío. Comparar contra `05-corridas.png`: se tiene que leer de corrido "el pipeline extrajo bien, el envío no pudo entregar".

- [ ] **Step 3: Correr la suite completa**

Run: `dotnet test` → 163 verdes, sin regresiones.

- [ ] **Step 4: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Runs/Index.razor
git commit -m "feat(admin): corridas de envio en el historial (TAREA 00038)"
```

---

### Task 12: Cierre

- [ ] **Step 1: Verificar los comentarios de versionado**

Run: `git diff main~11 --stat` y revisar que **cada archivo tocado** tenga su `// Fecha: 12/09/2026 - TAREA: 00038 - Linea: N - ...` en el bloque correspondiente, con la `Linea` correcta (1 = entidad/push, 2 = encadenamiento, 3 = Admin).

- [ ] **Step 2: Verificar el diseño contra lo construido**

Abrir `docs/Diseño/00038-envio-sql/envio-sql.md` y contrastarlo con el código. Si algo se desvió, **actualizar el diseño en este mismo commit**: un diseño viejo confunde más que no tenerlo. Prestar atención a las decisiones D1 y D2 de este plan, que no estaban en el spec original y hay que incorporarlas ahí.

- [ ] **Step 3: Actualizar los dos registros**

En `~/.claude/TAREAS.md`, reemplazar la fila "En curso" de 00038 por la definitiva: qué se construyó, qué quedó sin probar en vivo (la escritura real contra un SQL Server), y los pendientes.

En `docs/Historial.md`, agregar la entrada de la TAREA 00038 con el pedido textual y lo ejecutado.

- [ ] **Step 4: Commit final**

```bash
git add docs/Historial.md docs/Diseño/00038-envio-sql/
git commit -m "docs(historial): TAREA 00038 — envio a SQL externo"
```

---

## Pendientes conocidos al terminar

Anotar en `docs/Historial.md`, no dejarlos sólo acá:

1. **La escritura real contra un SQL Server queda sin probar en vivo.** No hay servidor en el entorno de desarrollo; `SqlServerDestinoSql` está testeado puro y `SqlServerDestino` es una cáscara fina, pero la ruta completa hay que probarla antes de producción. Mismo criterio que TAREA 00026.
2. **"Filas esperando"** en el form no tiene endpoint que lo calcule (Task 10).
3. **Ack global:** `_procesado` sigue sin identidad de consumidor. Este diseño lo esquiva, no lo arregla.
4. **Deuda de nombre:** `DataSource` usado como destino.
5. **El tipo de columna queda fijado en la primera corrida** (decisión D2). Si cambia el cast del pipeline, hay que borrar la tabla remota a mano.
6. **Si se implementa el grupo de pipelines** del borrador de encadenamiento, el disparo del envío debe colgarse del cierre del grupo, no de cada miembro.
