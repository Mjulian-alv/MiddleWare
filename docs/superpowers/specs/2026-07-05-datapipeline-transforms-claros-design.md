# DataPipeline — Paso 3 (Transformaciones) claro y bien definido — Design

**Fecha:** 2026-07-05
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.
**Se apoya en:** el editor wizard de 3 pasos ya en `main`.

## Que resuelve y por que

El Paso 3 del editor vuelca las 4 operaciones de transformacion (renombrar, convertir tipo, columna calculada, filtrar) como editores clave/valor genericos, sin rotulos ni explicacion. El usuario no sabe **que** puede hacer ni **como**: el tipo de cast se tipea a mano (texto libre), no hay ayuda de la sintaxis de las expresiones, y no se ve el orden en que se aplican. Se rediseña la presentacion para que las 4 operaciones queden claras y bien definidas, **sin tocar el motor** (`ColumnMapTransform` no cambia).

## Que soporta el motor hoy (contexto, no cambia)

`ColumnMapTransform` aplica por fila, en **orden fijo**: `renombrar → convertir tipo → calcular → filtrar`. Se pueden **encadenar** varios bloques. Las operaciones:
- **Renombrar:** columna origen → nuevo nombre.
- **Convertir tipo (cast):** columna → uno de 7 tipos exactos: `int`, `long`, `decimal`, `double`, `bool`, `date`, `string`.
- **Columna calculada:** nueva columna = expresion NCalc (las columnas son variables).
- **Filtrar:** una expresion booleana NCalc; pasan solo las filas donde da verdadero.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Alcance | **Aclarar las 4 operaciones existentes** (UI). Sin cambios de motor ni de datos guardados. |
| 2 | Nombre del bloque | "Paso N" (que choca con los pasos del wizard) pasa a **"Bloque de transformacion N"**. Se mantiene encadenar/quitar/reordenar bloques. |
| 3 | Presentacion | Las 4 operaciones **siempre visibles**, cada una rotulada + una linea de que hace. Arriba del bloque, el orden fijo. |
| 4 | Tipos de cast | **Dropdown** con los 7 tipos exactos (no texto libre). |
| 5 | Columnas | Donde se referencia una columna (renombrar/convertir), un **combo de las columnas descubiertas (Paso 1) + entrada libre** (por columnas renombradas/calculadas de otro bloque). |
| 6 | Ayuda | Bloque de **ayuda de sintaxis NCalc** (operadores, `if`, funciones comunes). |

## Arquitectura

Todo en `DataPipeline.Admin`. Dos archivos:

### `Components/Shared/KeyValueEditor.razor` — se extiende (reusable)

Hoy renderiza filas [clave (textbox) → valor (textbox)]. Se agregan parametros opcionales para cubrir los 3 usos:
- `[Parameter] string? Descripcion` — linea de ayuda bajo el titulo.
- `[Parameter] List<string>? ClaveOpciones` — si viene, la clave es un combo (SfComboBox con custom permitido) poblado con esas opciones; si no, textbox (como hoy).
- `[Parameter] List<string>? ValorOpciones` — si viene, el valor es un dropdown/combo con esas opciones; si no, textbox (como hoy).

El modelo de datos (`KV { Clave, Valor }`) y el guardado **no cambian**: cast sigue guardando el string del tipo en `Valor`, ahora restringido a valores validos por el dropdown.

### `Components/Pages/Pipelines/Editor.razor` — se rediseña el markup del bloque (Paso 3)

Sin tocar el `@code` (el modelo `TransformForm`, `ConstruirDef`, `Cargar` y los helpers de transforms quedan igual). Por cada bloque:
- Encabezado "Bloque de transformacion N" + botones subir/bajar/quitar (ya existen) + la linea de orden fijo.
- 4 secciones rotuladas, cada una con `KeyValueEditor` (o input, para filtrar) configurado:
  - **Renombrar columnas** — `KeyValueEditor Items=t.Rename ClaveOpciones=NombresColumnas` (clave=columna combo; valor=nuevo nombre texto). Descripcion: "Cambia el nombre de una columna."
  - **Convertir tipo** — `KeyValueEditor Items=t.Cast ClaveOpciones=NombresColumnas ValorOpciones=<7 tipos>` (clave=columna combo; valor=tipo dropdown). Descripcion: "Convierte el valor al tipo elegido."
  - **Columna calculada** — `KeyValueEditor Items=t.Computed` (clave=nueva columna texto; valor=expresion texto). Descripcion: "Crea una columna nueva con una expresion." Ejemplo inline: `precio * 1.21`.
  - **Filtrar filas** — el `SfTextBox` de `t.Filter` (una sola expresion). Descripcion: "Solo pasan las filas donde la expresion es verdadera." Ejemplo: `monto > 0`.
- **Ayuda NCalc** (al pie del bloque, o una vez por seccion de transformaciones): operadores `+ - * / % > < >= <= == != and or not`, condicional `if(cond, a, b)`, funciones comunes `Round, Abs, Max, Min, Floor, Ceiling`, + enlace a la doc de NCalc. (Se verifican los nombres exactos contra NCalc al implementar.)

La lista de 7 tipos de cast se define como una constante en el `@code` (`_tiposCast = new() { "int","long","decimal","double","bool","date","string" }`) y se pasa como `ValorOpciones`.

## Runtime / datos — sin cambios

No se toca `ColumnMapTransform` ni el guardado. Un pipeline con transforms se guarda y lee exactamente igual (maps rename/cast/computed + filter). El cast dropdown produce los mismos strings que hoy se tipean.

## Testing

- **Build-only / verificacion manual:** es UI Blazor/Syncfusion (los unit tests los escribe el usuario). Verificacion = build limpio (0 warnings) + prueba manual: crear un bloque, usar las 4 operaciones (tipo por dropdown, columna por combo), guardar y reabrir, confirmar que el pipeline corre igual que antes.

## Impacto / fuera de alcance

- Toca solo `DataPipeline.Admin` (`KeyValueEditor.razor` extendido + el markup del bloque en `Editor.razor`).
- **Fuera de alcance:** cambios al motor (`ColumnMapTransform`); sumar operaciones nuevas con nombre (trim, concat, formato de fecha, etc. — quedo como posible etapa 2); validacion en vivo de expresiones NCalc; el calculo de columnas post-transform disponibles en los combos de bloques encadenados (los combos ofrecen las columnas del origen + entrada libre; no se simula la cadena). Renovacion integral de la UI del Admin (tarea aparte, ver `admin-ui-refresh-pendiente`).
