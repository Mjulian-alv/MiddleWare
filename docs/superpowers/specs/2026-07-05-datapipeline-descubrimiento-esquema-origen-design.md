# DataPipeline — Descubrimiento de esquema del origen ("traer columnas") — Design

**Fecha:** 2026-07-05
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.
**Se apoya en:** Fase 4C (watermark) y el esquema idempotente al arranque (ambos ya en `main`).

## Que resuelve y por que

Hoy el origen de un pipeline "no conoce" sus columnas hasta que corre: se descubren en la primera corrida y ahi se crea la tabla del serving. Por eso, en el editor, **columna watermark** y **columnas clave** son texto libre — el usuario tipea nombres a mano, sin una lista contra la cual validar, con riesgo de equivocarse (un typo en el nombre del watermark hace que nunca avance; una clave mal escrita rompe el upsert/ack).

Se agrega un paso de **descubrimiento de esquema en tiempo de diseno** (patron del Admin de ConsultasGenericas): un boton "Traer columnas" corre el `SELECT` del origen en modo **solo-esquema** (sin bajar datos), captura las columnas del resultado, y las usa para poblar los selectores de watermark/clave y validar. Es una **ayuda de diseno**: el motor de corridas NO cambia.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | El origen conoce su esquema en diseno | Si, **descubierto**: se corre el SELECT solo-esquema y se capturan las columnas. |
| 2 | Alcance del esquema descubierto | **Ayuda de diseno**: llena los selectores de watermark/clave y valida. El serving se sigue creando en runtime desde el resultado real. El motor NO se toca. |
| 3 | Persistencia | **Persistido** en el pipeline (`EsquemaOrigen`), como el `ResultSchemaJson` de ConsultasGenericas. |
| 4 | Como se descubre | `ExecuteReader(CommandBehavior.SchemaOnly)` con `@watermark` bindeado en `DBNull` si el SQL lo referencia (misma guarda que `SqlServerSource`). Trae nombres + tipos, sin datos. |
| 5 | Watermark vs clave (pre/post-transform) | Watermark: dropdown de columnas del **origen** (pre-transform, correcto). Clave: multi-select de columnas del **origen** + validacion; el calculo de nombres post-transform queda **diferido**. |

## Modelo de datos

`PipelineDefinition` suma:
- `EsquemaOrigen` (`List<ColumnaOrigen>`): las columnas descubiertas del resultado del origen. Vacio = todavia no se descubrio.

Nuevo tipo (en `DataPipeline.Core`):
```csharp
public sealed class ColumnaOrigen
{
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = ""; // nombre del tipo SQL (ej. "datetime", "int", "nvarchar")
}
```

Persistencia EF: `EsquemaOrigen` como columna JSON TEXT via value-converter + comparer (mismo patron que `Pasos`/`CorrerDespuesDe`/`ColumnasClave` en `MetadataDbContext`). Nueva columna en `metadata.db`:
- Propiedad en el modelo EF + migracion EF (para el snapshot, segun la convencion vigente).
- **Linea `ALTER TABLE "Pipelines" ADD COLUMN "EsquemaOrigen" TEXT NOT NULL DEFAULT '[]'` en `MetadataSchemaInitializer.Sentencias`** (fuente de verdad del esquema en runtime; el test drift-guard `Crea_esquema_completo...` la exige) + su columna en el `CREATE TABLE IF NOT EXISTS "Pipelines"`.

## Descubrimiento (backend)

Abstraccion nueva (en `DataPipeline.Core.Abstractions` o `Connectors`, siguiendo el patron de `IConnectionTester`):
```csharp
public interface ISchemaDiscoverer
{
    // Devuelve las columnas del resultado de 'sql' sobre 'connectionString', sin traer datos.
    Task<IReadOnlyList<ColumnaOrigen>> DescubrirAsync(
        string connectionString, string sql, bool connectionStringEncrypted, IEncryptionService encryption, CancellationToken ct);
}
```
Impl `SqlServerSchemaDiscoverer` (en `DataPipeline.Connectors`):
- Desencripta el connection string si corresponde (como `SqlServerSource`).
- `await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 0 };`
- Si `sql` contiene `@watermark` (OrdinalIgnoreCase) y no esta ya bindeado, agregar `@watermark = DBNull.Value` (misma guarda que `SqlServerSource`).
- `await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);` → no ejecuta el query para datos, solo expone metadata.
- Leer las columnas: `rd.GetName(i)` + tipo via `rd.GetColumnSchema()` (o `rd.GetFieldType(i)` / `rd.GetDataTypeName(i)`) → mapear a `ColumnaOrigen`.

Endpoint nuevo (rol admin): `POST /api/datasources/{id:guid}/columnas`, body `{ "sql": "..." }`, devuelve `[{ "nombre": "...", "tipo": "..." }]`.
- Resuelve el `DataSource` por id (reusa el mismo camino que `/probar`: `IDataSourceStore` + desencriptado), y delega en `ISchemaDiscoverer`.
- Errores (SQL invalido, conexion caida) → 400 con el mensaje, para que el editor lo muestre.

> Testeabilidad: la abstraccion hace el endpoint/servicio unit-testeable con un `ISchemaDiscoverer` fake. La impl real (`SchemaOnly` contra SQL Server) se valida **manual**.

## Editor (Admin) — build-only

En la seccion **Origen** de `Editor.razor`:
- Boton **"Traer columnas"** (habilitado si hay `DataSourceId` + `Sql`). Al click, llama al endpoint via un servicio HttpClient nuevo (o extiende el existente), y guarda el resultado en el form (`_columnas`).
- **Columna watermark**: pasa de `SfTextBox` a **`SfDropDownList`** poblado con `_columnas` (mostrando el tipo; las de tipo fecha/entero marcadas como buenas candidatas). Tolera un valor guardado que no este en la lista (lo muestra marcado, para pipelines viejos).
- **Columnas clave**: pasa de texto separado por coma a **`SfMultiSelect`** poblado con `_columnas`.
- Al **guardar**: persiste `EsquemaOrigen` (las columnas descubiertas) ademas de watermark/clave.
- Al **cargar** un pipeline existente: repuebla `_columnas` desde `EsquemaOrigen` persistido (sin re-descubrir), repuebla los selectores, y **valida** que el watermark/clave guardados sigan existiendo → avisa inline si alguno ya no esta.
- Si el usuario cambia el SQL, puede volver a "Traer columnas" para refrescar (no hay auto-refresh en v1).

## Runtime — sin cambios

El `PipelineRunner`/`SqliteDataStore` siguen igual: el serving se crea en la corrida desde el resultado real (`EnsureTableAsync`). `EsquemaOrigen` es metadata de diseno (ayuda + validacion), no participa del runtime. Esto preserva la decision 2.

## Testing

- **Unit (TDD):**
  - Round-trip de `EsquemaOrigen` en `MetadataDbContext`/`EfPipelineStore` (persistencia JSON).
  - El drift-guard del schema-init (`Crea_esquema_completo...`) cubre la columna nueva al poblarla en el test.
  - El endpoint `/columnas` con un `ISchemaDiscoverer` fake (devuelve columnas fijas) → 200 con la lista; DataSource inexistente → 404; discoverer que tira → 400.
- **Manual / build-only:** la extraccion real via `SchemaOnly` contra SQL Server; los selectores/boton del editor.

## Impacto / fuera de alcance

- Toca: `DataPipeline.Core` (`ColumnaOrigen`, `PipelineDefinition.EsquemaOrigen`, `ISchemaDiscoverer`), `MetadataDbContext` (+migracion), `MetadataSchemaInitializer` (ALTER + CREATE), `DataPipeline.Connectors` (`SqlServerSchemaDiscoverer`), `DataPipeline.Api` (endpoint + DI), `DataPipeline.Admin` (editor + servicio), tests.
- **Fuera de alcance:** calcular los nombres **post-transform** para el selector de clave (aplicar el mapa de renames) — diferido, v1 ofrece las columnas del origen + validacion; hacer el esquema **autoritativo** sobre el serving (se decidio "ayuda de diseno"); discoverers para otros motores (MySQL/Oracle/Excel/RestApi) — hoy solo SQL Server; auto-refresh del esquema al editar el SQL; renovacion de la UI del Admin (tarea aparte, ver memoria `admin-ui-refresh-pendiente`).
