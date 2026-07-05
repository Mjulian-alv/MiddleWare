# DataPipeline — Inicializacion de esquema idempotente (metadata) — Design

**Fecha:** 2026-07-05
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.

## Que resuelve y por que

Hoy el esquema de `metadata.db` (almacen A) se crea con **EF Core `EnsureCreated()`** en el arranque de la API y del CLI. `EnsureCreated()` solo crea la base **si no existe**; sobre una base ya existente **no aplica ningun cambio de esquema**. Consecuencia real: al agregar columnas nuevas (p. ej. `ColumnaWatermark`/`UltimoWatermark` en Fase 4C), una `metadata.db` creada en una fase anterior queda sin esas columnas y la app falla en runtime con `SQLite Error 1: no such column: p.ColumnaWatermark`. La unica salida hoy es **borrar la base** (perdiendo pipelines/datasources), y cada cambio de esquema futuro (4B/4E/…) repetiria el problema.

Adoptamos el patron ya probado en el proyecto hermano **ConsultasGenericas** (`D:\Sistemas\ConsultasGenericas\ConsultasGenericas.Api\Program.cs`): en el arranque se corre **DDL idempotente directo** (`CREATE TABLE IF NOT EXISTS` + `ALTER TABLE ADD COLUMN`), cada sentencia envuelta en un try/catch que traga los errores "duplicate column"/"already exists". Es **seguro sobre una base existente** (no borra datos) y **auto-cura** el esquema: agregar una columna nueva es appendear una linea `ALTER` que se aplica sola en el proximo arranque y es inofensiva al re-ejecutarse.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Mecanismo de esquema | DDL idempotente al arranque (patron ConsultasGenericas). Reemplaza `EnsureCreated()`. |
| 2 | Alcance | Solo `metadata.db` (tablas `Origenes`, `Pipelines`, `Corridas`). El serving (`data.db` / `SqliteDataStore`) NO cambia: ya crea sus tablas dinamicamente por pipeline. |
| 3 | Donde vive | Un inicializador compartido en `DataPipeline.Persistence`, llamado por la API y el CLI (DRY, una sola fuente del DDL). |
| 4 | Migraciones EF | Se **conservan** (documentan el snapshot del modelo y mantienen usable `dotnet ef`), pero **ya no se aplican en runtime**. El inicializador DDL pasa a ser la fuente de verdad del esquema en runtime. |
| 5 | Enfoque descartado | EF `Database.Migrate()` (las bases existentes no tienen `__EFMigrationsHistory` → intentaria `InitialCreate` y fallaria por "table already exists"; requeriria baselining de cada base). Y `GenerateCreateScript()` (solo emite `CREATE TABLE`, nunca `ALTER ADD COLUMN` → no auto-cura columnas nuevas). |

## Arquitectura

### Componente nuevo: `MetadataSchemaInitializer` (en `DataPipeline.Persistence`)

Responsabilidad unica: garantizar que el esquema de `metadata.db` este al dia, de forma idempotente y segura sobre bases existentes.

```csharp
public static class MetadataSchemaInitializer
{
    // Abre (si hace falta) la conexion del DbContext y ejecuta el DDL idempotente.
    public static async Task EnsureAsync(DbConnection connection, ILogger? logger, CancellationToken ct);
}
```

- Recibe la `DbConnection` del `MetadataDbContext` (via `db.Database.GetDbConnection()`), la abre si no esta abierta, y ejecuta la lista ordenada de sentencias DDL.
- Cada sentencia en su propio try/catch:
  - traga `ex.Message` que contenga `"duplicate column"` o `"already exists"` (caso normal sobre base existente),
  - loguea `LogWarning` cualquier otro error (sin tumbar el arranque).
- `logger` es opcional (el CLI puede no tener uno; en ese caso, sin log).

### Lista de DDL (fuente de verdad del esquema en runtime)

Derivada exactamente del snapshot EF actual (`MetadataDbContextModelSnapshot`). Orden: primero los `CREATE TABLE IF NOT EXISTS` + indices con el **esquema completo actual** (para bases nuevas), luego los `ALTER TABLE ADD COLUMN` de las columnas agregadas despues del esquema inicial (para que una base vieja se ponga al dia). Ver Apendice A por las sentencias exactas.

Regla para columnas requeridas (NOT NULL) agregadas por ALTER: SQLite exige `DEFAULT` al agregar una columna NOT NULL sobre una tabla con filas. `ColumnasClave` (requerida) se agrega como `TEXT NOT NULL DEFAULT '[]'` — `'[]'` es JSON valido para el value-converter `List<string>` (evita que el deserializador falle al leer filas viejas; mejora respecto del `DEFAULT ''` de la migracion original). Las columnas nullable (`ColumnaWatermark`, `UltimoWatermark`) se agregan sin default.

### Wiring en los dos arranques (reemplaza `EnsureCreated`)

- **API** — `DataPipeline.Api/Program.cs` (hoy linea 42): en el scope de arranque, en vez de `...MetadataDbContext>().Database.EnsureCreated();`, obtener el `MetadataDbContext`, su `DbConnection`, un `ILogger<Program>`, y llamar `await MetadataSchemaInitializer.EnsureAsync(cn, logger, ct)`.
- **CLI** — `DataPipeline.Cli/Program.cs` (hoy lineas 23 y 69, dos usos de `EnsureCreatedAsync()` sobre el `MetadataDbContext`): reemplazar por `EnsureAsync`. (El uso del CLI sobre el store de **datos** — si existe — no se toca; solo el de metadata.)

> Nota: los tests siguen usando `EnsureCreated()` en sus fixtures (patron repo-wide). Este cambio es solo para los arranques reales (API/CLI). Los tests del inicializador usan su propia base temporal (ver Testing).

## Flujo de datos / casos

1. **Base nueva (no existe el archivo):** `CREATE TABLE IF NOT EXISTS` crea las 3 tablas con el esquema completo actual. Los `ALTER ADD COLUMN` posteriores fallan con "duplicate column" (las columnas ya estan) y se tragan. Resultado: esquema al dia.
2. **Base vieja (existe, sin columnas nuevas — el caso del bug actual):** `CREATE TABLE IF NOT EXISTS` es no-op (las tablas ya estan). Los `ALTER ADD COLUMN` agregan las columnas que faltan (`ColumnaWatermark`, `UltimoWatermark`, y `ColumnasClave` si faltara). Resultado: la base existente se pone al dia sin perder datos.
3. **Re-arranque (base ya al dia):** todo es no-op / tragado. Idempotente.

## Testing (en `DataPipeline.Persistence.Tests`)

Base temporal SQLite propia por test (mismo patron de archivos temporales que los tests existentes), pero construida **con `EnsureAsync`** (no `EnsureCreated`).

1. **Esquema completo + round-trip (drift guard):** base nueva → `EnsureAsync` → con un `MetadataDbContext` sobre esa base, insertar y releer un `PipelineDefinition`, un `DataSource` y un `PipelineRun` **con todas sus propiedades pobladas**. Si el DDL olvida una columna, el insert de EF falla → el test falla. Este es el guardarril anti-drift.
2. **Idempotencia:** correr `EnsureAsync` dos veces sobre la misma base → sin excepcion; el round-trip sigue funcionando.
3. **Auto-cura de columna faltante:** crear a mano una tabla `Pipelines` SIN `ColumnaWatermark` (un `CREATE TABLE` reducido), correr `EnsureAsync`, y verificar que la columna ahora existe (via `PRAGMA table_info('Pipelines')` o insertando/leyendo una fila que use la columna).

## Impacto / fuera de alcance

- Toca: nuevo `MetadataSchemaInitializer` (Persistence), `DataPipeline.Api/Program.cs`, `DataPipeline.Cli/Program.cs`, tests nuevos en Persistence.Tests.
- NO toca: `MetadataDbContext` (el modelo EF sigue igual), el serving (`data.db`/`SqliteDataStore`), las migraciones EF existentes (se conservan, sin aplicarse en runtime), los fixtures de tests (siguen con `EnsureCreated`).
- **Costo de mantenimiento asumido (decision 4):** al cambiar el modelo en el futuro hay que agregar la linea `ALTER ADD COLUMN` correspondiente en `MetadataSchemaInitializer`. El test #1 (drift guard) lo detecta si se olvida.
- Beneficio inmediato: la `metadata.db` actual del usuario se pone al dia en el proximo arranque (los `ALTER` agregan `ColumnaWatermark`/`UltimoWatermark`) sin borrarla ni perder el DataSource en configuracion.

## Apendice A — DDL exacto (derivado del snapshot EF actual)

Tipos SQLite segun `MetadataDbContextModelSnapshot`: `Guid`/`DateTime`/enums(string)/JSON → `TEXT`; `bool`/`int`/`long` → `INTEGER`. `IsRequired()` → `NOT NULL`; nullable → sin `NOT NULL`.

```sql
-- Origenes (DataSource)
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
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Origenes_Nombre" ON "Origenes" ("Nombre");

-- Pipelines (PipelineDefinition)
CREATE TABLE IF NOT EXISTS "Pipelines" (
    "Id"              TEXT    NOT NULL CONSTRAINT "PK_Pipelines" PRIMARY KEY,
    "Nombre"          TEXT    NOT NULL,
    "Habilitado"      INTEGER NOT NULL,
    "Programacion"    TEXT    NOT NULL,
    "HoraDelDia"      TEXT    NULL,
    "DiaDeLaSemana"   INTEGER NULL,
    "DiaDelMes"       INTEGER NULL,
    "ProximaCorrida"  TEXT    NULL,
    "UltimaCorrida"   TEXT    NULL,
    "Pasos"           TEXT    NOT NULL,
    "TablaDestino"    TEXT    NOT NULL,
    "ModoEscritura"   TEXT    NOT NULL,
    "CorrerDespuesDe" TEXT    NOT NULL,
    "ColumnasClave"   TEXT    NOT NULL DEFAULT '[]',
    "ColumnaWatermark" TEXT   NULL,
    "UltimoWatermark" TEXT    NULL,
    "FilasPorLote"    INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Pipelines_Nombre"         ON "Pipelines" ("Nombre");
CREATE INDEX        IF NOT EXISTS "IX_Pipelines_ProximaCorrida" ON "Pipelines" ("ProximaCorrida");

-- Corridas (PipelineRun)
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
);
CREATE INDEX IF NOT EXISTS "IX_Corridas_Inicio" ON "Corridas" ("Inicio");

-- Columnas agregadas despues del esquema inicial (auto-cura de bases viejas).
-- Sobre base nueva fallan con "duplicate column" (tragado). Requeridas llevan DEFAULT.
ALTER TABLE "Pipelines" ADD COLUMN "ColumnasClave"    TEXT NOT NULL DEFAULT '[]';
ALTER TABLE "Pipelines" ADD COLUMN "ColumnaWatermark" TEXT NULL;
ALTER TABLE "Pipelines" ADD COLUMN "UltimoWatermark"  TEXT NULL;
```

> Los nombres de constraint de PK (`PK_Origenes`, etc.) son cosmeticos en SQLite; se incluyen por consistencia con lo que genera EF. El orden de columnas dentro del `CREATE` no afecta a EF (mapea por nombre).
