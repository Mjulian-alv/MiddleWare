# Fase 4 — Roadmap y brief de decisiones (para brainstorming)

> **Por que este doc:** la Fase 4 (Confiabilidad y encadenamiento) es multi-subsistema. Cada
> rebanada necesita decisiones de diseno del usuario antes de codear (4A demostro que adivinar el
> diseno lleva a rehacer todo). Este brief deja cada rebanada lista para un brainstorming corto:
> proposito, decisiones abiertas, opciones con recomendacion, dependencias y gotchas.
> **No es un spec** (eso se escribe al brainstormear cada una). Fecha base: 2026-06-22.

## Estado al cierre de hoy
- Fases 1, 2, 3 completas. **Fase 4A completa** (serving con ciclo de procesado: `_key`/`_creado`/
  `_procesado`, upsert por key en Reemplazar, limpieza en Agregar, GET filtrado + `POST /procesar` ack).
- Demo sin SQL Server: `dotnet run --project DataPipeline.Cli -- seed-demo <metadata.db> <data.db>` +
  GET/ack con `pull:pull123`.
- Build pristine, 58 tests verdes. Todo en `main` (sin remoto).

---

## Orden sugerido
Recomiendo: **4C (watermark) -> 4B (reintentos) -> 4E (dead-letter) -> 4F (encadenamiento) ->
4G (2do source) -> SqlServerDataStore (si hace falta)**. Razon: el watermark (4C) cierra el ciclo
incremental del lado del ORIGEN (complementa el ciclo de procesado del lado del consumidor que ya
hizo 4A); los reintentos (4B) dependen de resolver la interaccion con la idempotencia; el resto se
apoya en esos cimientos.

> 4D (idempotencia/upsert) quedo en gran parte **absorbido por 4A** (upsert por key). Lo que queda de
> 4D es menor y se puede plegar en 4B/4E.

---

## 4C — Carga incremental (watermark) del lado del ORIGEN

**Que resuelve:** no releer todo el origen cada corrida; traer solo lo nuevo desde el ultimo valor
procesado (`WHERE col > @watermark`).

**Decisiones abiertas:**
1. **Como se declara el watermark.**
   - (A, recomendada) Campo en el pipeline `ColumnaWatermark` (nombre de columna del origen, ej.
     `fecha_mod` o `id`). El editor lo captura.
   - (B) Convencion en el SQL del usuario (un `@watermark` que el pone donde quiera).
2. **Como se aplica el filtro.**
   - (A, recomendada) El runner pasa `@watermark` como parametro y el SQL del usuario lo usa
     (`WHERE fecha_mod > @watermark`). Explicito, sin reescribir SQL.
   - (B) El runner envuelve/reescribe el SQL para inyectar el filtro (fragil).
3. **Donde se guarda el ultimo watermark.**
   - (A, recomendada) Campo en `PipelineDefinition` (`UltimoWatermark`, string/valor), persistido
     tras cada corrida exitosa.
   - (B) Tabla aparte de watermarks.
4. **Primera corrida / reseteo:** watermark inicial nulo => trae todo; boton "resetear watermark".

**Gotcha:** el tipo del watermark (fecha vs entero) y el formato. Recomiendo guardarlo como string y
que el `SqlServerSource` lo bindee al tipo del parametro.

**Recomendacion:** A/A/A. Toca `PipelineDefinition` (+migracion), `SqlServerSource` (acepta params),
`RunContext.Parameters` (ya existe), runner (guarda el max tras correr) y editor (columna + SQL con
`@watermark`).

---

## 4B — Reintentos (Polly) + clasificacion transitorio/permanente

**Que resuelve:** que un fallo transitorio (red, deadlock, timeout) no tumbe la corrida; reintentar
con backoff exponencial.

**Decisiones abiertas (la 1 es critica):**
1. **Alcance del reintento vs idempotencia (GOTCHA serio).**
   - Reintentar **toda la corrida** duplica datos en **Agregar** (si ya se escribieron lotes antes
     del fallo). En **Reemplazar** (upsert por key) es idempotente -> seguro.
   - (A, recomendada) Reintentar toda la corrida **solo para Reemplazar**; para Agregar, reintentar
     solo si el fallo ocurrio **antes de escribir el primer lote** (conexion/lectura inicial).
   - (B) Reintentar siempre toda la corrida (simple, pero duplica en Agregar).
   - (C) Hacer Agregar idempotente-en-reintento (escribir a staging y swap, o tag de run-id y limpiar
     parciales) -> mas trabajo, habilita reintento full seguro en ambos modos.
2. **Politica:** nro de intentos (def 3), backoff (exponencial 1s/2s/4s), jitter si/no.
3. **Clasificacion transitorio:** `SqlException` (numeros de error de red/deadlock/timeout),
   `TimeoutException`, errores de conexion. Permanente: el resto (datos, NotSupported, validacion).
4. **Donde vive:** en `EjecutorProgramado` y en el run on-demand (`RunsController`), o dentro de
   `PipelineRunner`. Recomiendo un decorador/polizas en el punto de orquestacion.

**Recomendacion:** decision 1 = A (seguro), o C si se quiere robustez total (mas trabajo). Paquete
`Polly`. Configurable por pipeline (intentos) con default global.

---

## 4E — Dead-letter

**Que resuelve:** filas/lotes que fallan no frenan el resto; van a un almacen aparte para revision.

**Decisiones abiertas:**
1. **Granularidad:** dead-letter por **fila** (mas fino, mas lento) o por **lote**.
2. **Donde:** tabla `_deadletter_{pipeline}` en el mismo serving SQLite, con la fila + error + corrida.
3. **Disparador:** ¿que cuenta como "fila fallida"? Hoy un error en transform/escritura tumba la
   corrida. Dead-letter implica capturar por fila/lote y continuar.
4. **UI/observabilidad:** ¿se ven en el Admin? (pantalla `/deadletter` futura).

**Recomendacion:** por lote, tabla en el serving, visible en el historial. Depende de definir el
manejo de errores por-lote en el runner (hoy es all-or-nothing).

---

## 4F — Encadenamiento (`RunAfter`)

**Que resuelve:** correr el pipeline B cuando A termina OK (el editor ya guarda `CorrerDespuesDe`,
pero su ejecucion no existe).

**Decisiones abiertas:**
1. **Disparo:** el `EjecutorProgramado`/scheduler, al completar A con exito, encola los que dependen
   de A. ¿Inmediato o en el proximo tick?
2. **Gate/timeout por dependencia:** ¿B espera a A con timeout? ¿que pasa si A falla (B no corre)?
3. **`DataStoreSource`:** B suele leer del serving que dejo A (un source que lee de SQLite). ¿Se
   construye en esta rebanada?
4. **Ciclos:** detectar dependencias circulares.

**Recomendacion:** empezar simple — al completar A OK, marcar los dependientes como vencidos
(ProximaCorrida=now); `DataStoreSource` como source nuevo que lee el serving. Circuit breaker:
diferido.

---

## 4G — 2do source heterogeneo (MySQL/Oracle/Excel/RestApi)

**Que resuelve:** que el dropdown de tipos de origen sea funcional (hoy solo SqlServer corre).

**Decisiones abiertas:**
1. **Cual primero** (segun el negocio): MySQL / Oracle / Excel / RestApi.
2. **Reuso de ConsultasGenericas:** ¿calcar sus ejecutores (drivers) o reimplementar streaming?
3. Por cada tipo: `XSource : ISource` + dispatch en `SourceFactory` por tipo del DataSource +
   tester en `ConnectionTester` + el editor habilita el tipo.

**Recomendacion:** elegir 1 tipo segun necesidad real; es trabajo de motor por conector.

---

## SqlServerDataStore configurable (diferido en 4A)

Quedo diferido porque el serving es intermedio (SQLite alcanza) y el destino final lo hace el
consumidor. **Reabrir solo si** algun consumidor necesita leer el serving por SQL directo (no por
GET). Si se reabre: tendria que replicar el ciclo de procesado (`_key`/`_creado`/`_procesado` +
upsert/ack) en SQL Server, o ser un sink "push final" separado (que el usuario dijo que es del
consumidor). Baja prioridad.

---

## Para retomar manana
Elegir una rebanada (recomiendo 4C), responder sus decisiones abiertas, y arranco
brainstorming -> spec -> plan -> implementacion (TDD, commits a main), como hoy con 4A.
