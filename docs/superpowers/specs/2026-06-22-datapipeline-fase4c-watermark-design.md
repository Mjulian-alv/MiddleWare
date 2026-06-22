# DataPipeline — Fase 4C (Carga incremental / watermark) — Design

**Fecha:** 2026-06-22
**Estado:** aprobado (decisiones validadas con el usuario).
**Rebanada:** Fase 4C. Precede a su plan de implementacion.

## Que es y por que

Hoy cada corrida lee **todo** el origen. El watermark permite traer **solo lo nuevo** desde la
ultima corrida, usando una columna monotona del origen (una fecha de modificacion o un id
incremental). Es una optimizacion para tablas grandes/que crecen, y evita duplicados en modo
Agregar (releer todo + Agregar = duplica).

> No confundir con el historial de corridas (`/runs`): eso trackea *cuando* corrio cada pipeline.
> El watermark trackea *hasta que dato del origen* se llego.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Como se declara | Campo `ColumnaWatermark` en el pipeline (la columna del origen a trackear). |
| 2 | Como se aplica el filtro | **El SQL del usuario usa `@watermark`** (contrato explicito). El pipeline NO reescribe el SQL. |
| 3 | Donde se guarda el ultimo valor | Campo `UltimoWatermark` (string) en `PipelineDefinition`. |
| 4 | Primera corrida / reset | `UltimoWatermark` null -> el SQL del usuario (`@watermark IS NULL OR ...`) trae todo. Reset = vaciar el campo desde el editor. |

## Modelo de datos

`PipelineDefinition` suma:
- `ColumnaWatermark` (string?): nombre de la columna del **resultado del origen** cuyo maximo se
  trackea. Vacio = el pipeline no es incremental.
- `UltimoWatermark` (string?): ultimo maximo visto (persistido tras cada corrida exitosa).

Migracion EF (dos columnas nullable; converter no hace falta, son strings simples).

## Contrato (de dos partes)

1. En el pipeline se declara `ColumnaWatermark` (ej. `fecha_mod`).
2. El **SQL del origen** referencia `@watermark`, ej.:
   ```sql
   SELECT id, sucursal, monto, fecha_mod
   FROM ventas
   WHERE @watermark IS NULL OR fecha_mod > @watermark
   ```
   El usuario controla el operador (`>` vs `>=`) y el casteo. El binding de `@watermark` ya existe:
   `SqlServerSource` bindea `ctx.Parameters` como `SqlParameter`s.

## Mecanica (en `PipelineRunner`)

1. **Inyectar:** antes de correr, si `ColumnaWatermark` esta seteada, **siempre** agregar
   `ctx.Parameters["watermark"]`. Si `UltimoWatermark` es null (primera corrida / reset), igual se
   bindea como `DBNull` (NO omitir el parametro: el SQL referencia `@watermark` y SQL Server exige
   que este declarado; el `@watermark IS NULL` del usuario maneja el caso). Nota: `ctx.Parameters`
   es `IReadOnlyDictionary<string,string>` y `SqlServerSource` bindea `null`/ausente como `DBNull`,
   asi que basta con setear la clave (o dejar que el binding existente mapee null -> DBNull).
2. **Trackear el maximo:** mientras streamea, en la etapa del **origen** (antes de los transforms,
   con el nombre original de la columna — porque un transform podria renombrarla), llevar el maximo
   de `fila.Values[ColumnaWatermark]` (ignorando nulls). Comparacion con `Comparer<object>.Default`
   (los valores de una columna vienen tipados y homogeneos del origen).
3. **Devolver:** `RunResult` suma `NuevoWatermark` (string?). Formato de guardado: `DateTime` en
   ISO 8601 round-trip (`"O"`, ej. `2026-06-21T18:00:00`) para que SQL Server lo parsee sin
   ambiguedad; otros tipos con `Convert.ToString(InvariantCulture)`.
4. **Persistir:** el que orquesta la corrida persiste `UltimoWatermark`:
   - `EjecutorProgramado` (scheduler) y `RunsController` (on-demand), tras una corrida exitosa con
     `NuevoWatermark != null`, llaman a un metodo nuevo del store.
   - `IPipelineStore` suma `Task ActualizarWatermarkAsync(Guid id, string? watermark, CancellationToken ct)`.

> Solo se persiste el watermark si la corrida fue exitosa (`RunResult.Success`). Si fallo, no se
> avanza (la proxima corrida reintenta desde el mismo punto). Si `Success` pero `NuevoWatermark` es
> null (no vino ninguna fila), no se toca el watermark guardado.

## Editor (3E) — build-only

- Suma un input **"Columna watermark (opcional)"** -> `ColumnaWatermark`.
- Ayuda inline: "Si la usas, tu SQL debe filtrar con @watermark (ej. WHERE @watermark IS NULL OR
  fecha_mod > @watermark)".
- Reset: vaciar el campo y guardar (la proxima corrida trae todo). `UltimoWatermark` no se edita a
  mano; se puede ofrecer un boton "resetear watermark" que lo limpia (opcional, simple).

## Ejemplo concreto

Pipeline `ventas`, `ColumnaWatermark = "fecha_mod"`, modo Agregar, SQL:
`... WHERE @watermark IS NULL OR fecha_mod > @watermark`.

- **Corrida 1:** `UltimoWatermark` null -> trae todo. Maximo `fecha_mod` visto = `2026-06-20T18:00:00`.
  Se persiste `UltimoWatermark = 2026-06-20T18:00:00`.
- Entran 50 ventas nuevas (`fecha_mod` de hoy).
- **Corrida 2:** se bindea `@watermark = 2026-06-20T18:00:00` -> el origen trae solo las 50 nuevas.
  Nuevo maximo `2026-06-21T10:30:00` -> se persiste.
- Si la corrida 2 **falla**, `UltimoWatermark` queda en `2026-06-20T18:00:00` y la corrida 3 reintenta
  desde ahi.

## Testing

- **TDD (con `FakeSource`, sin SQL Server):**
  - El runner trackea el maximo de `ColumnaWatermark` y lo devuelve en `RunResult.NuevoWatermark`.
  - Inyecta `UltimoWatermark` en `ctx.Parameters["watermark"]` (verificable con un source fake que
    inspecciona los parametros).
  - `EjecutorProgramado` persiste el watermark tras corrida exitosa (con repos EF reales).
  - No se persiste si la corrida falla.
- **Manual / build-only:** el binding real de `@watermark` en `SqlServerSource` ya existe (se prueba
  contra SQL Server real); el input del editor es build-only.

## Impacto / fuera de alcance

- Toca `PipelineDefinition` (+migracion), `RunResult`, `PipelineRunner`, `IPipelineStore`
  (+EfPipelineStore), `EjecutorProgramado`, `RunsController`, editor.
- No cambia el contrato de `ISource`/`IDataStore` (reusa `ctx.Parameters`).
- Fuera de alcance: watermark compuesto (varias columnas), tipado fuerte del watermark (queda como
  string + responsabilidad del SQL del usuario castear), ventanas/solapamiento (`>=` con dedup) —
  el operador lo decide el usuario en su SQL.
