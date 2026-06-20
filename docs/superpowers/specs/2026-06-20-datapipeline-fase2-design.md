# DataPipeline — Fase 2 (Motor usable + entrega PULL) — Diseño

> **Estado:** diseño aprobado. Listo para escribir el plan de implementación.
> **Fecha:** 2026-06-20.
> **Base:** `ANALISIS-MIDDLEWARE.md` (secciones 6–13, 15, 20) y la Fase 1 ya construida.
> **Convención de idioma:** Opción 2 — props y enums en castellano; interfaces, tipos técnicos de runtime, servicios y métodos en inglés; sin tildes/ñ en identificadores (sí en comentarios). Ver "Mapa de nombres" en el plan de Fase 1.

## 1. Objetivo

Convertir la Fase 1 (streaming SqlServer→SQLite por JSON) en un middleware **usable**: transformaciones config-driven, pipelines persistidos y programados, y los datos transformados **expuestos por una API PULL autenticada**, con historial de corridas.

**Criterio de éxito:** un pipeline definido y persistido en la BD, programado (Diaria/Semanal/Mensual), corre solo (scheduler), aplica transformaciones (rename/cast/filter/computed), aterriza en su tabla, y otro sistema obtiene esos datos por `GET /api/data/{nombre}` autenticado; cada corrida queda en el historial.

## 2. Decisiones tomadas

| # | Decisión | Resolución |
|---|---|---|
| 1 | Dónde viven las definiciones | **En la BD (EF Core, almacén A)** como fuente de verdad; JSON queda como formato de import/export. |
| 2 | Evaluador de expresiones (filter/computed) | **NCalc** (en `Connectors`/`Engine`, no en `Core`). |
| 3 | Auth de la API PULL | **Basic Auth contra credenciales en configuración** (sin tabla `ApiUser` todavía; eso es Fase 3). |
| 4 | Persistencia: proyecto | **Proyecto `DataPipeline.Persistence` aparte** (EF Core), implementando abstracciones de repositorio definidas en `Core`. Engine/scheduler dependen de las abstracciones, no de EF. |
| 5 | Tipo de `StepDefinition.Configuracion` | Evoluciona de `Dictionary<string,string>` a **`Dictionary<string, JsonElement>`** para soportar configs anidadas (transforms). Retrocompatible en el JSON del usuario; el `SqlServerSource` pasa a leer `Configuracion["sql"].GetString()`. |
| 6 | Endpoints extra en Fase 2 | Se incluyen **`POST /api/run/{pipelineId}`** (on-demand) y **`GET /api/runs`** (historial), además del PULL. |
| 7 | Almacenes en dev | Almacén A (`metadata.db`) y almacén B (`data.db`) en **archivos SQLite separados**, configurables. |

## 3. Arquitectura

```
DataPipeline/
├── Core          (+ ITransformFactory; PipelineRun + EstadoCorrida; IPipelineStore; IHistorialCorridas)
├── Connectors    (+ ColumnMapTransform [NCalc]; TransformFactory)
├── Engine        (PipelineRunner compone transforms; + ScheduleCalculator)
├── Persistence   ← NUEVO: EF Core almacén A — MetadataDbContext, repos, migraciones
├── Api           ← NUEVO: deployable — endpoints PULL/run/runs, Basic Auth, SchedulerHostedService
└── Cli           (+ comando "import": JSON → BD)
```

Dependencias: `Connectors→Core`; `Engine→Core(+Connectors)`; `Persistence→Core` (+ EF Core); `Api→Engine+Connectors+Persistence+Core`; `Cli→Engine+Connectors+Persistence+Core`.

## 4. Persistencia (almacén A, EF Core) — `DataPipeline.Persistence`

- **Mapeo de `PipelineDefinition`:** los escalares a columnas reales (`Id`, `Nombre`, `Habilitado`, `Programacion`, `HoraDelDia`, `DiaDeLaSemana`, `DiaDelMes`, `ProximaCorrida`, `UltimaCorrida`, `TablaDestino`, `ModoEscritura`, `FilasPorLote`); las colecciones `Pasos` (List<StepDefinition>) y `CorrerDespuesDe` (List<Guid>) como **columnas JSON** vía `ValueConverter` (configurado en el DbContext con Fluent API, así `Core` no depende de EF).
- **`PipelineRun`** (nueva entidad en `Core`): `Id` (Guid), `PipelineId` (Guid), `Inicio` (DateTime), `Fin` (DateTime?), `Estado` (`EstadoCorrida { EnCurso, Ok, Fallida }`), `FilasLeidas` (long), `FilasEscritas` (long), `FilasError` (long), `Mensaje` (string?).
- **Abstracciones de repositorio (en `Core`):**
  - `IPipelineStore`: `GetByIdAsync(Guid)`, `GetByNombreAsync(string)`, `GetAllAsync()`, `UpsertAsync(PipelineDefinition)`, `ObtenerVencidosAsync(DateTime ahora)` (habilitados con `ProximaCorrida <= ahora`), `ActualizarProgramacionAsync(Guid id, DateTime? ultima, DateTime? proxima)`.
  - `IHistorialCorridas`: `IniciarAsync(Guid pipelineId)` → Guid runId; `CompletarAsync(Guid runId, RunResult result)`; `ObtenerRecientesAsync(int max)`.
- **`MetadataDbContext`** (en `Persistence`): `DbSet<PipelineDefinition>`, `DbSet<PipelineRun>` + Fluent config (converters JSON, claves, índices en `Nombre` y `ProximaCorrida`). Migraciones EF (`dotnet ef migrations add`). SQLite (`metadata.db`) en dev.
- Implementaciones `EfPipelineStore`/`EfHistorialCorridas` en `Persistence`.

## 5. Transformaciones (config-driven + NCalc)

- **`ColumnMapTransform : ITransform`** (en `Connectors`), lee de `StepDefinition.Configuracion`:
  - `rename`: objeto `{ "origen": "destino" }` → renombra columnas.
  - `cast`: objeto `{ "col": "decimal|int|date|string|bool" }` → convierte tipos (con cultura invariante; nulos pasan como null).
  - `filter`: string expresión NCalc booleana evaluada por fila (`"importe > 0"`); las filas que dan false se descartan.
  - `computed`: objeto `{ "col": "expresión NCalc" }` → agrega/recalcula columnas (`"monto - bonificacion"`).
  - **Orden de operaciones** (fijo y testeado): `rename → cast → computed → filter` (ver §13). Procesa **lote a lote** (streaming): por cada `RecordBatch` reescribe columnas/filas y reemite. El esquema de columnas de salida se deriva de rename/computed.
- **`ITransformFactory`** (Core) + **`TransformFactory`** (Connectors): resuelve `step.Tipo == TipoPaso.Transformacion` y `step.Conector == "ColumnMapTransform"` → la transform; conector desconocido → `NotSupportedException`.
- **`PipelineRunner`** (Engine): se quita el `throw NotSupportedException` de transforms; resuelve los pasos `Transformacion` en orden y compone:
  ```
  stream = source.ReadAsync(ctx, ct);
  foreach (var t in transforms) stream = t.TransformAsync(stream, ctx, ct);
  await foreach (batch in stream) { EnsureTable(primera vez); WriteBatch; }
  ```
- **Evolución de contrato:** `StepDefinition.Configuracion` pasa a `Dictionary<string, JsonElement>`. Ajuste en Fase 1: `SourceFactory`/`SqlServerSource` leen `Configuracion["x"].GetString()`. El JSON de los sources no cambia.

## 6. Scheduler

- **`ScheduleCalculator`** (Engine, estático): dado `PipelineDefinition` + `desde`, calcula `ProximaCorrida` para `Diaria`/`Semanal`/`Mensual` usando `HoraDelDia`/`DiaDeLaSemana`/`DiaDelMes`. `BajoDemanda` → no programa (`ProximaCorrida = null`). Molde de `EmailScheduleCalculator`. Unit-testeado.
- **`PipelineSchedulerHostedService`** (Api, `BackgroundService`): delay inicial ~15s; loop cada ~60s:
  1. `IPipelineStore.ObtenerVencidosAsync(DateTime.Now)`.
  2. Por cada pipeline: crear `scope`, `IniciarAsync` corrida, correr `PipelineRunner`, `CompletarAsync(runId, result)`.
  3. **En `finally`:** recalcular `ProximaCorrida` con `ScheduleCalculator` y `ActualizarProgramacionAsync` (anti-bucle).
  4. `try/catch` por pipeline (uno que falla no frena a los demás).
- Molde directo del `EmailSchedulerHostedService` probado.

## 7. API (`DataPipeline.Api`) — el deployable

- **`GET /api/data/{nombre}?page=&pageSize=`** (Basic Auth): resuelve pipeline por `Nombre`; lee `TablaDestino` con `IDataStore.ReadPagedAsync` (paginado); devuelve `{ columns: [...], rows: [...] }`. 404 si el pipeline o su tabla no existen.
- **`POST /api/run/{pipelineId}`** (Basic Auth): dispara una corrida on-demand; devuelve el `RunResult` (RunId, Success, filas, error).
- **`GET /api/runs?max=`** (Basic Auth): historial reciente (`IHistorialCorridas.ObtenerRecientesAsync`).
- **Auth:** handler Basic Auth (`AuthenticationHandler`) que valida usuario/clave contra `appsettings`/secrets (`PullAuth:Username` / `PullAuth:PasswordHash` BCrypt, molde de ConsultasGenericas). Sin tabla de usuarios.
- **Host:** registra el `PipelineSchedulerHostedService`. Almacén A y B vía DI; `EncryptionService` con clave desde config (igual que ConsultasGenericas).

## 8. DI y configuración

- `Api/Program.cs`: `AddDbContext<MetadataDbContext>` (SQLite `metadata.db`); `IPipelineStore→EfPipelineStore`; `IHistorialCorridas→EfHistorialCorridas`; `ISourceFactory→SourceFactory`; `ITransformFactory→TransformFactory`; `IDataStore→SqliteDataStore` (`data.db`); `PipelineRunner`; `IEncryptionService→EncryptionService(config)`; Basic Auth; `AddHostedService<PipelineSchedulerHostedService>`.
- `appsettings.json`: cadenas de `metadata.db`/`data.db`, `Encryption:Key`, `PullAuth`.

## 9. CLI

- Comando `import <pipeline.json>`: carga con `PipelineJsonLoader`, calcula `ProximaCorrida` inicial con `ScheduleCalculator`, y hace `IPipelineStore.UpsertAsync`. Permite seedear la BD desde archivos. El modo "run directo" de Fase 1 se conserva.

## 10. Manejo de errores

- Corrida: excepción → `RunResult(Success:false, Error)` + `PipelineRun` `Fallida` con `Mensaje`. `FilasLeidas`/`FilasEscritas` reflejan el progreso hasta el fallo.
- API: 401 sin/!mal auth; 404 pipeline o tabla inexistente; 400 parámetros de paginado inválidos; 200 con datos.
- Transform: error de expresión NCalc (sintaxis/variable faltante) → falla la corrida con mensaje claro (qué paso, qué expresión).

## 11. Testing

- **Unit:** `ColumnMapTransform` (rename, cast por tipo, filter que descarta filas, computed; expresión inválida lanza), `ScheduleCalculator` (Diaria/Semanal/Mensual + bordes de hora/día), `TransformFactory` (resuelve/!soportado).
- **Repos (EF + SQLite real):** `EfPipelineStore` (upsert, GetByNombre, ObtenerVencidos por `ProximaCorrida`/`Habilitado`, ActualizarProgramacion), `EfHistorialCorridas` (Iniciar→Completar, ObtenerRecientes).
- **Runner:** pipeline con source fake + 1–2 transforms → tabla; verifica filas transformadas (rename/filter aplicados) vía `ReadPagedAsync`.
- **Integración API (`WebApplicationFactory`):** seed pipeline + tabla aterrizada → `GET /api/data/{nombre}` devuelve filas; sin auth → 401; pipeline inexistente → 404; `POST /api/run/{id}` corre y aparece en `GET /api/runs`.
- **Integración scheduler:** un pipeline vencido se corre una vez y su `ProximaCorrida` avanza; uno que falla no frena al resto y queda `Fallida` en historial.

## 12. Fuera de alcance (Fase 2)

- UI Admin (Fase 3). Watermark/incremental, Polly/reintentos, dead-letter, idempotencia/upsert real, `SqlServerDataStore`, encadenamiento `CorrerDespuesDe`/`DataStoreSource` (Fase 4). Entrega PUSH (Fase 5). SQL-staging transform y scripting (posterior).

## 13. Riesgos / notas

- **NCalc y tipos:** los valores de fila son `object?` (int/decimal/string/DateTime/null). Pasar parámetros a NCalc con cuidado de tipos; `cast` debería correr antes de `filter`/`computed` que dependan de tipos numéricos. Orden de operaciones dentro de `ColumnMapTransform`: rename → cast → computed → filter (documentado y testeado).
- **`Configuracion` JsonElement:** al venir de EF (columna JSON) y del loader, asegurar que ambos produzcan `JsonElement` consistentes (mismo `JsonSerializerOptions`).
- **Almacén A vs B:** mantener separación de archivos en dev evita locks cruzados entre el scheduler (escribe metadata) y la API PULL (lee datos).
