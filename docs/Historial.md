# Historial — DataPipeline (MiddleWare)

Registro de tareas ejecutadas en este repo, correlativas con `~/.claude/TAREAS.md`.

## TAREA 00014 / TAREA 00015 — 03-05/09/2026

**Pedido (item 1, un solo ítem):** Importar CSV y XLSX para llenar una base de datos con archivos
extraidos de un ERP viejo que no tiene conexion directa a SQL.

**Nota sobre la numeracion:** este pedido quedo repartido en dos TAREA por un choque de sesiones
concurrentes. Las Tasks 1-4 del plan ya se habian commiteado bajo **TAREA 00014** cuando se detecto
que otra sesion (proyecto ConsultasGenericas) tambien habia tomado ese numero al mismo tiempo. Se
decidio con el usuario no reescribir los commits ya hechos: Tasks 1-4 quedan con `TAREA: 00014` tal
cual estan, y las Tasks 5-11 (el resto del plan) usan **TAREA 00015**. Es un unico pedido, un unico
plan, documentado bajo dos correlativos por esa razon puntual.

**Spec:** `docs/superpowers/specs/2026-09-03-datapipeline-origenes-archivo-design.md`.
**Plan:** `docs/superpowers/plans/2026-09-03-datapipeline-origenes-archivo.md`.

**Ejecutado (11 tasks, subagent-driven, todas con review de spec + calidad, todas aprobadas):**

- **Task 1** (TAREA 00014): `TipoDataSource` gana `Csv`/`Xlsx`; `DataSource` reemplaza el `FilePath`
  sin uso por `RutaCarpeta`/`PatronArchivo`/`Separador`/`Encoding`/`TieneEncabezado`/`HojaExcel`;
  `PipelineDefinition` gana `ColumnaWatermarkTipo`. Esquema idempotente actualizado
  (`MetadataSchemaInitializer`, sin migracion EF nueva — las migraciones ya no se aplican en runtime).
- **Task 2** (TAREA 00014): se extrae `TipoValorConverter` (7 tipos: int/long/decimal/double/bool/
  date/string) desde `ColumnMapTransform`, para reusarlo al tipar la columna watermark de archivos.
- **Task 3** (TAREA 00014): `FileSourceBase` — logica comun de cualquier origen de archivo: descubre
  los no procesados de una carpeta, los ordena por fecha, filtra/tipa la columna watermark, y mueve
  cada archivo a `\procesados` recien cuando se leyo completo sin error (nunca se reprocesa un
  archivo, sea el export un dump acumulativo o un delta — no hace falta saber cual es).
- **Task 4** (TAREA 00014): `CsvSource` (libreria `CsvHelper`, no split a mano — un CSV de ERP viejo
  puede traer comillas con el separador adentro) + wiring en `SourceFactory`.
- **Task 5** (TAREA 00015): `XlsxSource` (libreria `ClosedXML`, mismo patron de lectura que
  `ExcelQueryExecutor` del proyecto madre ConsultasGenericas) + wiring en `SourceFactory`.
- **Task 6** (TAREA 00015): `IConnectionTester.ProbarArchivoAsync` (valida carpeta + permiso de
  escritura) y `ISchemaDiscoverer.DescubrirArchivoAsync` (lee el encabezado del archivo mas nuevo
  sin procesar) para que el Admin pueda "probar" y "traer columnas" de un origen de archivo.
- **Task 7** (TAREA 00015): `PipelineHydrator` inyecta la config de carpeta (no un secreto, a
  diferencia de la connection string) para origenes Csv/Xlsx antes de correr el pipeline.
- **Task 8** (TAREA 00015): `DataSourcesController` — campos nuevos en alta/edicion, `Probar`/
  `Columnas` ramificados por `Tipo`. Con esto el build de toda la solucion volvio a estar 100% limpio
  (se habia roto en la Task 1 por el `FilePath` eliminado).
- **Task 9** (TAREA 00015): pantalla "Origenes de datos" del Admin — tipo Csv/Xlsx seleccionable,
  campos condicionales por tipo (carpeta/patron siempre; separador/encoding/encabezado solo Csv;
  hoja solo Xlsx; secretos solo SqlServer).
- **Task 10** (TAREA 00015): wizard del Editor de pipelines — Paso 1 reacciona al tipo del origen
  elegido (sin SQL para archivos, dropdown de tipo de columna watermark). Durante la revision se
  encontro y corrigio un hueco real del propio plan: `ConstruirDef` no estaba inyectando
  `columnaWatermark`/`tipoColumnaWatermark` en la config del paso, asi que el filtro incremental de
  `FileSourceBase` quedaba como no-op aunque el usuario eligiera columna y tipo en la UI. Corregido
  y verificado antes de cerrar la task.
- **Task 11** (TAREA 00015): verificacion final — build de toda la solucion 0 warnings/0 errores,
  suite completa 99 tests verdes (15 Persistence + 62 Core/Connectors/Engine + 22 Api), cierre de
  este documento y de la fila en `~/.claude/TAREAS.md`.

**Fuera de alcance (explicito, decidido con el usuario):** MySQL/MariaDB/Oracle/RestApi (existen en
el proyecto madre ConsultasGenericas pero no se pidieron aca); upload manual de archivos por el
Admin (se eligio carpeta de red fija); deteccion automatica de si un archivo es dump acumulativo o
delta (el mecanismo de mover a `\procesados` lo resuelve sin necesitar saberlo).

**Pendiente del dueño:** verificacion manual end-to-end (crear un `DataSource` Csv y uno Xlsx, armar
un pipeline contra cada uno, correrlo y confirmar que el archivo se mueve a `\procesados` y que una
segunda corrida sin archivos nuevos no reprocesa nada) — el Admin es build-only por convencion de
este repo, sin tests automatizados de UI.
