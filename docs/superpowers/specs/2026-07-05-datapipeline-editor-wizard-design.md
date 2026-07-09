# DataPipeline — Editor de pipelines como wizard de 3 pasos — Design

**Fecha:** 2026-07-05
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.
**Se apoya en:** el descubrimiento de esquema del origen ("traer columnas") ya en `main`.

## Que resuelve y por que

El editor actual (`Editor.razor`) presenta todo en tarjetas planas en un orden que no sigue el flujo de los datos: *Columnas clave* y *Columna watermark* estan en la tarjeta **Generales** (arriba), pero solo tienen sentido **despues** de conocer las columnas del query, que se traen en la tarjeta **Origen** (abajo). El usuario elige clave/watermark antes de tener contra que elegirlas.

Se reorganiza el editor como un **wizard de 3 pasos guiados** que sigue el flujo real: primero el query y sus columnas, despues la salida (clave, destino), y por ultimo las transformaciones. El Paso 2 se habilita recien cuando el query fue "testeado" (columnas traidas OK), lo que evita estructuralmente el problema de hoy.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Estructura | **Wizard guiado**: un paso visible a la vez, con Atras/Siguiente y gating. |
| 2 | Que es "testear el query" | **Traer columnas** (solo-esquema, ya existe). Si devuelve columnas, el query es valido y conecta. Es el gate del Paso 1. Sin endpoint nuevo. |
| 3 | Meta del pipeline | *Nombre* + *Habilitado* en un **encabezado siempre visible**. *Programacion* en el Paso 3 (bloque compacto antes de Guardar). |
| 4 | Modo edicion | Al abrir un pipeline existente, los 3 pasos quedan **navegables desde el arranque**; las columnas salen del `EsquemaOrigen` persistido, sin forzar re-testear. En pipeline nuevo, el avance es gated. |
| 5 | Componente | **Stepper liviano propio** (3 encabezados + Atras/Siguiente), sin depender de `SfStepper`. |

## Arquitectura

Todo vive en el Admin (`DataPipeline.Admin`). `Editor.razor` orquesta el wizard (estado del paso activo, navegacion, gating, guardar) y delega el contenido de cada paso a **componentes hijo**, para que el archivo no siga creciendo:

- `Editor.razor` — shell del wizard: encabezado (Nombre/Habilitado), stepper, el paso activo, barra Atras/Siguiente/Guardar. Mantiene el `PipelineForm` (fuente de verdad del formulario) y `_pasoActivo` (1..3).
- `Components/Pages/Pipelines/Pasos/PasoOrigen.razor` — DataSource, SQL, boton "Traer columnas", columnas descubiertas, *Columna watermark*.
- `Components/Pages/Pipelines/Pasos/PasoSalida.razor` — columnas descubiertas (referencia), *Columnas clave*, *Modo de escritura*, *Tabla destino*, *Filas por lote*.
- `Components/Pages/Pipelines/Pasos/PasoTransforms.razor` — la cadena de transforms existente + *Programacion*.

Cada componente hijo recibe el `PipelineForm` por parametro (referencia compartida; el hijo muta sus propiedades) y, donde haga falta, inyecta directamente los servicios que ya usa hoy (p. ej. `PasoOrigen` inyecta `DataSourcesAdminService` para "Traer columnas"). El shell lee el `PipelineForm` para calcular el gating.

> El `PipelineForm`, `ConstruirDef()`, `Cargar()` y los helpers de transforms **no cambian de logica**; se mueven/reparten entre el shell y los hijos. Es una reorganizacion de presentacion, no de datos.

## Contenido y gating por paso

**Encabezado (siempre visible):** *Nombre* (obligatorio) + toggle *Habilitado*.

**Paso 1 — Origen y columnas**
- *DataSource*, *SQL*, boton **"Traer columnas"** (el test). Al traer OK: chips con las columnas (nombre + tipo) y se habilita **Columna watermark** (dropdown de esas columnas; fecha/entero marcadas como candidatas).
- **Gate P1 → P2:** `EsquemaOrigen` tiene columnas (traidas OK). Sin eso, "Siguiente" deshabilitado. (DataSource + SQL son implicitamente requeridos para poder traer.)

**Paso 2 — Salida**
- Columnas descubiertas (solo lectura, como referencia), **Columnas clave** (multi-select de esas columnas), **Modo de escritura** (Agregar/Reemplazar), **Tabla destino**, **Filas por lote**.
- **Gate P2 → P3:** *Tabla destino* no vacia + (si *Modo* = Reemplazar, al menos una *clave*).

**Paso 3 — Transformaciones (opcional) + Programacion**
- La cadena de transforms que ya existe (agregar/quitar/reordenar; rename/cast/computed/filter). Sin gate (opcional).
- **Programacion** (Bajo demanda/Diaria/Semanal/Mensual + sus campos) en un bloque compacto.
- **Guardar:** requiere *Nombre* (+ las validaciones actuales de `Guardar()`). Reusa el guardado actual.

En **modo edicion** todos los gates arrancan satisfechos (el pipeline ya es valido) y el usuario puede saltar entre pasos por los encabezados del stepper.

## Runtime / backend — sin cambios

No se toca la API, ni el motor, ni la persistencia. Se reusa el endpoint `POST /api/datasources/{id}/columnas` (traer columnas) y el guardado actual (`PipelinesAdminService.GuardarAsync` + `ConstruirDef`). `EsquemaOrigen`, `ColumnaWatermark` y `ColumnasClave` se persisten como hoy.

## Testing

- **Build-only / verificacion manual:** es UI Blazor/Syncfusion (los unit tests los escribe el usuario). La verificacion es build limpio (0 warnings) + prueba manual del flujo: nuevo pipeline gated (Paso 2 bloqueado hasta traer columnas), edicion navegable, guardar y reabrir.

## Impacto / fuera de alcance

- Toca solo `DataPipeline.Admin` (`Editor.razor` + 3 componentes hijo nuevos).
- **Fuera de alcance:** cambios de backend/motor; el calculo de nombres post-transform para la clave (sigue diferido); vista previa de filas del query (se descarto: el test es "traer columnas"); la renovacion integral de la UI del Admin desde ConsultasGenericas (tarea aparte, ver memoria `admin-ui-refresh-pendiente` — este wizard es un paso acotado y compatible con esa futura renovacion).
