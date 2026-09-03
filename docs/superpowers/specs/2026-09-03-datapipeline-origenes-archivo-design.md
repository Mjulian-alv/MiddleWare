# DataPipeline — Origenes de archivo (CSV y XLSX) — Design

**Fecha:** 2026-09-03
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.
**Se apoya en:** `SqlServerSource`/`SourceFactory`/`RecordBatcher` (Fase 2), `DataSource`/`PipelineHydrator` (Fase 3B), watermark generico en `PipelineRunner` (Fase 4C).

## Que resuelve y por que

Hoy DataPipeline solo tiene un origen: `SqlServerSource`. El caso real a resolver es un ERP viejo sin conexion SQL directa: alguien extrae archivos (CSV o Excel) a mano y los deja en una carpeta de red; DataPipeline tiene que poder leer esos archivos y cargar una base de datos igual que ya hace con SQL Server, incluyendo carga incremental.

Se reviso el proyecto madre `ConsultasGenericas` (`D:\proyectos\consultasgenericas`) para no reinventar lo ya resuelto: tiene un conector Excel (`ExcelQueryExecutor`, via `ClosedXML`) pero **no** tiene CSV, y su Excel es mucho mas simple que lo que hace falta aca (apunta a **un** archivo fijo por `FilePath`, sin carpeta, sin mover a procesados, sin incremental — sirve datos de referencia estaticos via API, no ingesta de exports historicos). Se toma de ahi la libreria (`ClosedXML`) y el patron de lectura (primera fila = encabezado); todo lo demas (carpeta, patron, mover a procesados, filtrado por watermark) es especifico de este caso de uso y no existe en el proyecto madre. Los otros conectores de CG (MySQL, MariaDB, Oracle, RestApi) quedan fuera de alcance: no son parte de este pedido.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Alcance | Dos origenes nuevos: `Csv` y `Xlsx`. MySQL/MariaDB/Oracle/RestApi del proyecto madre quedan afuera. |
| 2 | Entrega del archivo | Carpeta de red fija (`RutaCarpeta`), no upload manual por el Admin. |
| 3 | Nombre de archivo entre corridas | Archivo nuevo por extraccion, con nombre distinto (ej. `VENTAS_20260903.csv`) — no siempre el mismo archivo sobrescrito. |
| 4 | Contenido del archivo nuevo vs. el anterior | No se sabe con certeza si cada extraccion es un dump acumulativo completo o un delta — puede variar segun el caso. El diseño no asume ninguno de los dos (ver "Ciclo de vida del archivo"). |
| 5 | Tracking de procesados | Mover cada archivo ya leido con exito a una subcarpeta `\procesados` (no se guarda una marca en la base; es visible a simple vista y no requiere estado adicional). |
| 6 | Carga incremental | Por columna (watermark), igual filosofia que SQL, no recarga total. |
| 7 | Libreria XLSX | `ClosedXML` (MIT). Confirmada por el uso ya en produccion en CG. |
| 8 | Libreria CSV | `CsvHelper` (MIT) en vez de partir a mano por el separador: un CSV de un ERP viejo puede traer campos entre comillas con el separador o saltos de linea adentro, y un `Split` ingenuo rompe silenciosamente ahi. |
| 9 | Formato CSV | Separador, encoding y "tiene encabezado" configurables por `DataSource` (no fijo) — un ERP viejo tipicamente exporta con `;` y Windows-1252 (ANSI/Latin1), no `,`/UTF-8. |

## Arquitectura

### 1. Modelo de datos

**`TipoDataSource`** (`DataPipeline.Core/DataSources/DataSource.cs`) suma `Csv` y `Xlsx` a `SqlServer`.

**`DataSource`** reemplaza el campo `FilePath` (placeholder sin uso, comentado `// futuro (Excel)`) por los campos que realmente hacen falta para un feed de archivos:
- `RutaCarpeta` (`string?`) — carpeta de red donde el ERP deja los exports.
- `PatronArchivo` (`string?`) — glob para filtrar (ej. `VENTAS_*.csv`); vacio = `*.csv`/`*.xlsx` segun `Tipo`.
- `Separador` (`string?`) — solo `Csv` (`,` / `;` / tab). Default `,`.
- `Encoding` (`string?`) — solo `Csv` (`UTF-8` / `Windows-1252` [ANSI/Latin1]). Default `UTF-8`.
- `TieneEncabezado` (`bool`) — solo `Csv`. Default `true`.
- `HojaExcel` (`string?`) — solo `Xlsx`. Vacio = primera hoja del libro.

**`PipelineDefinition`** suma `ColumnaWatermarkTipo` (`string?`), uno de los mismos 7 valores que ya usa el dropdown de "Convertir tipo" en Paso 3 (`int/long/decimal/double/bool/date/string`). Vacio/nulo cuando no hay `ColumnaWatermark`. Solo se usa cuando el origen es `Csv`/`Xlsx` (ver seccion 3); para `SqlServer` se ignora porque ADO.NET ya entrega valores tipados.

Migracion EF nueva sobre `DataSources` (los 6 campos) y `Pipelines` (`ColumnaWatermarkTipo`), siguiendo el esquema idempotente ya establecido (DDL `CREATE IF NOT EXISTS`/`ALTER`).

### 2. Ciclo de vida del archivo

Por cada corrida, el conector:
1. Lista los archivos de `RutaCarpeta` que matchean `PatronArchivo` y **no** estan ya en `RutaCarpeta\procesados\`.
2. Los ordena por fecha de modificacion ascendente (mas robusto que asumir que el nombre siempre ordena bien).
3. Procesa cada uno en orden: lee filas, aplica el filtro de watermark si corresponde (seccion 3), yield por `RecordBatcher` igual que `SqlServerSource`.
4. Al terminar de leer un archivo **completo sin errores**, lo mueve a `\procesados` (crea la subcarpeta si no existe). Si ya hay un archivo con ese nombre ahi, le agrega un sufijo (no lo pisa).
5. Si falla la lectura de un archivo, no se mueve — queda en la carpeta de entrada para reintentarse en la proxima corrida (la corrida completa se marca fallida, igual que hoy con un error de SQL).

Este mecanismo — mover a `\procesados` apenas se procesa, y no antes — resuelve la incertidumbre de la decision #4 sin necesidad de saber si el archivo es un dump acumulativo o un delta: cada archivo se lee una unica vez en toda su vida, en ambos casos.

Si la corrida trae mas de un archivo nuevo (no corrio en un tiempo), se procesan todos, en orden, dentro de la misma corrida — el tracking de watermark de `PipelineRunner` ya acumula el maximo a lo largo de todo el stream, sea uno o varios archivos.

### 3. Filtrado incremental (watermark) y tipado

`PipelineRunner` ([PipelineRunner.cs:70-115](../../../DataPipeline.Engine/PipelineRunner.cs)) ya trackea el maximo de `ColumnaWatermark` **despues** de leer, de forma agnostica al origen. Lo que falta para archivos es el **filtrado** (no volver a traer filas viejas), que para SQL resuelve el usuario en su propio `WHERE @watermark ...` y que para archivos tiene que resolver el conector.

Ademas hay un riesgo de bug silencioso si no se tipa: un CSV es texto plano, y comparar como texto una fecha o un numero da resultados incorrectos (`"9" > "10"` como texto; `"2026-1-5"` ordena mal contra `"2026-12-1"`). Por eso `ColumnaWatermarkTipo` (seccion 1) le dice al conector como parsear **esa columna especifica** al construir cada `Record`:
- `CsvSource`/`XlsxSource` parsean el valor crudo de la columna watermark al tipo indicado (mismo mapeo que ya usa el cast del Paso 3) antes de devolver el `Record`.
- Con el valor ya tipado, tanto el filtro (`valor > ctx.Parameters["watermark"]`, parseando el string persistido con el mismo tipo) como el tracking de maximo que ya hace `PipelineRunner` (via `Comparer<object>.Default.Compare`) funcionan correctamente.
- Primera corrida (`UltimoWatermark` nulo): `ctx.Parameters` no trae `"watermark"` (mismo comportamiento que hoy) — no se filtra nada, se traen todas las filas de todos los archivos no procesados.
- El resto de las columnas (no la de watermark) se devuelven sin tipar (string para CSV, tipo nativo de la celda para XLSX) — si el usuario necesita mas tipos, ya existe "Convertir tipo" en Paso 3 para eso.

### 4. Conectores (`DataPipeline.Connectors`)

- **`FileSourceBase`** (nueva, abstracta) — la logica comun de la seccion 2 (descubrir, ordenar, mover a procesados) mas el filtrado de la seccion 3. Los conectores concretos solo implementan "parsear este archivo a filas".
- **`CsvSource : FileSourceBase`** — parsea con `CsvHelper`, usando `Separador`/`Encoding`/`TieneEncabezado` del `DataSource`. Si `TieneEncabezado` es falso, nombra columnas `Col1, Col2, ...` segun la cantidad de campos de la primera fila.
- **`XlsxSource : FileSourceBase`** — abre con `ClosedXML.XLWorkbook`, selecciona `HojaExcel` (o la primera hoja si viene vacio), primera fila = encabezado — mismo patron que `ExcelQueryExecutor` de CG.

Nuevas dependencias en `DataPipeline.Connectors.csproj`: `ClosedXML`, `CsvHelper` (ambas MIT).

`SourceFactory` ([SourceFactory.cs](../../../DataPipeline.Connectors/Factories/SourceFactory.cs)) suma los casos `"CsvSource"` y `"XlsxSource"` al switch, leyendo `rutaCarpeta`/`patronArchivo`/etc. de `Configuracion` (inyectados por `PipelineHydrator`, igual que hoy hace con `connectionString`).

### 5. Api (`DataPipeline.Api`)

**`DataSourcesController`**:
- `DataSourceUpsert` suma `RutaCarpeta`, `PatronArchivo`, `Separador`, `Encoding`, `TieneEncabezado`, `HojaExcel`.
- `Probar` (`POST /{id}/probar`) se ramifica por `Tipo`: para `Csv`/`Xlsx` valida que `RutaCarpeta` exista y sea escribible (necesario porque `\procesados` se crea ahi) en vez de abrir una conexion SQL. Nuevo `IConnectionTester.ProbarArchivoAsync`.
- `Columnas` (`POST /{id}/columnas`) se ramifica igual: para archivo no hace falta que el usuario escriba nada (el `DataSource` ya tiene todo lo necesario) — abre el archivo mas nuevo sin procesar y lee su encabezado. `ColumnasReq.Sql` pasa a ser opcional (solo obligatorio para `SqlServer`). Nuevo `ISchemaDiscoverer.DescubrirArchivoAsync(DataSource, ct)`.

**`PipelineHydrator`** ([PipelineHydrator.cs](../../../DataPipeline.Engine/PipelineHydrator.cs)) suma la rama para `Csv`/`Xlsx`: en vez de inyectar `connectionString`, inyecta `rutaCarpeta`/`patronArchivo`/`separador`/`encoding`/`tieneEncabezado`/`hojaExcel` en `Configuracion` a partir del `DataSource` resuelto.

### 6. Admin (`DataPipeline.Admin`)

**Pantalla "Origenes de datos"** ([DataSources/Index.razor](../../../DataPipeline.Admin/Components/Pages/DataSources/Index.razor)): `_tipos` (hoy `{ SqlServer }` hardcodeado) suma `Csv`/`Xlsx`. El dialogo de alta/edicion muestra campos distintos segun el tipo elegido (mismo patron `@if (modelo.Type == ...)` que ya usa CG):
- `SqlServer`: sin cambios (ConnectionString/Usuario/Contraseña).
- `Csv`: Carpeta, Patron de archivo, Separador (dropdown), Encoding (dropdown), checkbox "Tiene encabezado".
- `Xlsx`: Carpeta, Patron de archivo, Hoja (opcional).

**Wizard del Editor de pipelines** ([Editor.razor](../../../DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor)), Paso 1: hoy siempre pide SQL y fija `Conector = "SqlServerSource"`. Como `_datasources` ya trae el `Tipo` de cada origen (`DataSourceVista.Tipo`), el paso reacciona:
- `SqlServer`: igual que hoy.
- `Csv`/`Xlsx`: se oculta el textbox de SQL; "Traer columnas" no pide nada extra; aparece un segundo dropdown "Tipo de columna watermark" (mismos 7 valores que `_tiposCast`) junto al dropdown de columna watermark que ya existe, visible solo cuando hay columna watermark elegida.
- `ConstruirDef()` arma `Conector` como `"SqlServerSource" | "CsvSource" | "XlsxSource"` segun el tipo del `DataSource` elegido, y solo agrega `sql` a la config cuando corresponde.

## Testing

- **Core/Connectors/Engine/Api:** con tests unitarios, siguiendo el patron ya establecido (`SqlServerSource`, `PipelineHydrator`, etc. tienen cobertura). `CsvSource`/`XlsxSource`/`FileSourceBase` son testeables con archivos temporales (crear un CSV/XLSX de prueba, correr, verificar filas + que el archivo se movio a `\procesados`). Casos a cubrir: descubrimiento + orden de archivos, filtrado por watermark (con y sin valor previo), mover a procesados (incluida colision de nombre), error de lectura no mueve el archivo, encoding/separador no default, hoja inexistente.
- **Admin (Blazor/Syncfusion):** build-only, sin unit tests (los escribe el usuario, por convencion del repo). Verificacion = build limpio (0 warnings) + prueba manual: crear un `DataSource` Csv y uno Xlsx, "Probar", armar un pipeline apuntando a cada uno, correrlo dos veces (la segunda no debe reprocesar lo ya movido a `\procesados`).

## Impacto / fuera de alcance

Toca `DataPipeline.Core` (enum + campos nuevos), `DataPipeline.Connectors` (2 conectores nuevos + 2 dependencias), `DataPipeline.Persistence` (migracion), `DataPipeline.Engine` (`PipelineHydrator`), `DataPipeline.Api` (`DataSourcesController`, `IConnectionTester`, `ISchemaDiscoverer`) y `DataPipeline.Admin` (DataSources + Editor).

**Fuera de alcance:** MySQL/MariaDB/Oracle/RestApi (existen en el proyecto madre pero no se pidieron aca); upload manual de archivos por el Admin (se decidio carpeta de red fija); tipado de columnas mas alla de la watermark (ya cubierto por "Convertir tipo" en Paso 3); deteccion automatica de si un archivo es dump acumulativo o delta (no hace falta, ver seccion "Ciclo de vida del archivo"); locking/coordinacion si dos corridas del mismo pipeline se superponen (limitacion preexistente, no introducida por esto).
