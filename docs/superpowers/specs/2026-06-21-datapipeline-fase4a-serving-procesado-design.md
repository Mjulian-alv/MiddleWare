# DataPipeline — Fase 4A (Serving con ciclo de procesado) — Design

**Fecha:** 2026-06-21
**Estado:** aprobado (decisiones validadas con el usuario).
**Rebanada:** Fase 4A (reencuadrada). Precede a su plan de implementacion.

## Contexto y reencuadre

DataPipeline es PULL: origen -> transform -> **tabla de serving (intermedia)** que administra el
middleware -> el consumidor hace `GET /api/data/{pipeline}` y se lleva los datos a su destino final.
**El destino final lo decide el consumidor**, no el middleware.

Originalmente 4A iba a ser "destino SQL Server configurable". Se **reencuadra**: el serving es
intermedio y SQLite alcanza; lo valioso es darle al serving un **ciclo de vida de procesado** para
que el consumidor extraiga **incrementalmente** y confirme lo consumido. El `SqlServerDataStore`
configurable queda **diferido** a una rebanada futura (solo si algun consumidor necesita leer la
tabla por SQL directo en vez de por GET).

## Objetivo

Que cada tabla de serving tenga columnas de control (`_key`, `_creado`, `_procesado`) y que:
1. El consumidor lea por GET solo lo **no procesado** (incremental).
2. El consumidor **confirme** (ack) lo que tomo, marcandolo procesado.
3. Cada corrida administre el ciclo segun el modo de escritura (limpieza en Agregar; upsert por key
   en Reemplazar).

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Que es 4A | Serving con ciclo de procesado (no SqlServerDataStore configurable). |
| 2 | Donde vive el serving | SQLite (intermedio). |
| 3 | Identidad de fila (key) | Columnas de negocio que el usuario elige en el editor (`ColumnasClave`). |
| 4 | Marcar procesado | Ack **explicito**: endpoint que recibe las keys que el consumidor tomo. |
| 5 | Reemplazar: keys ausentes | Upsert puro: no se borra nada (coincide->refresca; nueva->inserta). |

## Modelo de datos

- `PipelineDefinition` suma `List<string> ColumnasClave` (columnas de negocio que forman la clave).
  Requerida para Reemplazar; opcional para Agregar.
- Columnas de control que el store agrega a cada tabla de serving (nombres reservados con prefijo
  `_` para no chocar con columnas de negocio):
  - **`_key`** (texto): combinacion determinista de los valores de `ColumnasClave`
    (join de los valores con separador de unidad `U+001F`). Si `ColumnasClave` esta vacia y el modo
    es Agregar, `_key` = un GUID (surrogate) por fila para poder ackear.
  - **`_creado`** (datetime): momento en que el middleware escribio/actualizo la fila.
  - **`_procesado`** (entero 0/1): arranca en 0; el consumidor lo lleva a 1 via ack.

## Escritura por modo (en `SqliteDataStore`)

Por corrida, el runner prepara la tabla y escribe los lotes:

- **Agregar (append + limpieza):**
  1. Asegura la tabla (la crea con columnas de negocio + control si no existe).
  2. **Borra las filas `_procesado=1`** (limpieza, para no acumular lo ya consumido).
  3. Inserta cada fila del resultado con `_creado=now`, `_procesado=0`, `_key` calculada.
- **Reemplazar (upsert por `_key`, sin borrar ausentes):**
  1. Asegura la tabla (no la dropea).
  2. Por cada fila: si su `_key` ya existe -> UPDATE (campos de negocio + `_creado=now`,
     `_procesado=0`); si no existe -> INSERT (`_creado=now`, `_procesado=0`).
  3. Las keys que ya no vienen **quedan** (upsert puro).
- **Combinar:** sigue `NotSupported`.

> Nota: esto evoluciona el contrato `IDataStore` (hoy `EnsureTableAsync`/`WriteBatchAsync`/
> `ReadPagedAsync`). La escritura pasa a recibir `ColumnasClave` + `ModoEscritura`; se agrega lectura
> filtrada por `_procesado` y un metodo para marcar procesadas. Las firmas exactas se fijan en el
> plan. El `PipelineRunner` pasa `def.ColumnasClave` y `def.ModoEscritura` al store.

## Lectura + ack (API)

- **`GET /api/data/{nombre}`** (auth `pull`): por defecto devuelve solo filas `_procesado=0`,
  incluyendo `_key` y `_creado`, paginado (`page`/`pageSize` existentes). Parametro opcional
  `?incluirProcesados=true` para devolver todas (debug). Forma de respuesta: `{ columns, rows }`
  (como hoy), ahora con las columnas de control incluidas.
- **`POST /api/data/{nombre}/procesar`** (auth `pull`): body `{ "keys": ["...", "..."] }` con las
  `_key` que el consumidor tomo -> marca esas filas `_procesado=1`. Devuelve la cantidad marcada.
  Ack explicito: si el consumidor falla entre el GET y el ack, nada se pierde (siguen
  `_procesado=0`).

## Editor (3E)

- Suma un input **"Columnas clave"** (lista de nombres de columna del resultado). Requerido cuando
  `ModoEscritura = Reemplazar` (el upsert lo necesita); opcional en Agregar.
- Se persiste en `PipelineDefinition.ColumnasClave` (round-trip por el JSON, como el resto).

## Ejemplo concreto

Pipeline `ventas`, `ColumnasClave = ["id_venta"]`, resultado con columnas `id_venta, monto`.

**Modo Agregar:**
- Corrida 1 escribe 3 filas:

  | _key | id_venta | monto | _creado | _procesado |
  |------|----------|-------|---------|------------|
  | 1 | 1 | 100 | 10:00 | 0 |
  | 2 | 2 | 200 | 10:00 | 0 |
  | 3 | 3 | 300 | 10:00 | 0 |

- Consumidor: `GET` -> recibe las 3 (todas `_procesado=0`). Las guarda de su lado.
- Consumidor: `POST /procesar { keys: ["1","2"] }` -> filas 1 y 2 quedan `_procesado=1`.
- Corrida 2: borra las `_procesado=1` (1 y 2), inserta lo nuevo (id_venta 4):

  | _key | id_venta | monto | _creado | _procesado |
  |------|----------|-------|---------|------------|
  | 3 | 3 | 300 | 10:00 | 0 |
  | 4 | 4 | 400 | 11:00 | 0 |

- Consumidor: `GET` -> recibe 3 y 4 (lo pendiente + lo nuevo).

**Modo Reemplazar (upsert):**
- Corrida 1: igual que arriba (3 filas, `_procesado=0`).
- Consumidor procesa la 1 (`_procesado=1`).
- Corrida 2: viene id_venta 2 con monto cambiado (250), id_venta 1 igual, id_venta 3 ya no viene, y
  aparece id_venta 4:
  - key 1 coincide -> UPDATE (`_creado=11:00`, `_procesado=0`) aunque estaba procesada (vuelve a 0).
  - key 2 coincide -> UPDATE monto=250, `_creado=11:00`, `_procesado=0`.
  - key 3 ausente -> queda como estaba.
  - key 4 nueva -> INSERT.

  | _key | id_venta | monto | _creado | _procesado |
  |------|----------|-------|---------|------------|
  | 1 | 1 | 100 | 11:00 | 0 |
  | 2 | 2 | 250 | 11:00 | 0 |
  | 3 | 3 | 300 | 10:00 | 0 |
  | 4 | 4 | 400 | 11:00 | 0 |

## Testing

- **TDD normal** (SQLite real, sin SQL Server): los cambios en `SqliteDataStore` (columnas de
  control, Agregar con limpieza, Reemplazar con upsert, lectura filtrada, marcar procesadas) y los
  endpoints (`GET` filtrado + `POST /procesar`) se testean con SQLite + `WebApplicationFactory`.
- El editor (UI "Columnas clave") queda **build-only** (consistente con Fase 3).

## Impacto en otras rebanadas

- Absorbe el **upsert por key** que estaba previsto en 4D (idempotencia); 4D se reduce.
- `SqlServerDataStore` configurable: diferido (rebanada futura, opt-in).
- No toca watermark del lado del origen (4C) ni reintentos (4B) ni encadenamiento (4F).

## Fuera de alcance

- Borrado de keys ausentes en Reemplazar (se eligio upsert puro).
- Consume-on-read (se eligio ack explicito).
- Hash de la key (se eligio columnas elegidas; el `_key` es el join de sus valores).
- Destino final / escritura a sistemas externos (lo hace el consumidor).
