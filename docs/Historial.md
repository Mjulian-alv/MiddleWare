# Historial — DataPipeline (MiddleWare)

Registro de tareas ejecutadas en este repo, correlativas con `~/.claude/TAREAS.md`.

## TAREA 00157 — 02/11/2026

**Pedido (textual):** «podes modificar el proyecto d:\sistemas\middelware para que se integre con apphub» y, al
aclararlo: «lo que quiero que hagas es el trabajo de modificar los endpoint para que se pueda usar datapipeline.admin
en apphub» / «mismo trabajo que de hizo en conciliador» (TAREA 00140). Numeración: «igual que conciliador».

**Ítems (Linea):** 1 sección `AppHub` y validación del token `X-AppHub-Identidad` · 2 usuario de AppHub ·
3 sesión (sin «Salir», login propio como respaldo con aviso) · 4 correr detrás de AppHub (`PathBase`, https, rutas).

**Diseño previo:** `docs/Diseño/00157-identidad-apphub/` (guía, flujograma y 2 maquetas). Codex no pudo hacerlo (su
sandbox solo escribe en `D:\Sistemas\Concentrador` y su terminal no arranca): el flujograma y las maquetas los hizo Claude.

**Decisión de diseño:** el Admin tiene un solo usuario fijo (`dpAdmin`), sin usuarios ni permisos. **Quien AppHub deja
abrir entra como administrador**: excepción al punto 4 del contrato («el primer ingreso nunca da permisos»). Elegida por
quien hizo el pedido.

**Qué se hizo** (rama `tarea-00157-identidad-apphub`, `DataPipeline.Admin`):
- `TokenAppHub` e `IdentidadAppHub` (middleware), portados del Conciliador. **No hay cookie**: la sesión del Admin vive en
  el circuito de Blazor, así que el middleware deja la identidad como claims en `HttpContext.User` y Blazor la toma del
  pedido que crea el circuito. `AdminSessionState.InicializarAsync` la lee. Sin `AppHub:ClaveCompartida` todo sigue igual.
- `MainLayout` oculta «Salir» si la sesión vino de AppHub; `Login` muestra «AppHub no pudo confirmar tu identidad».
- **`PathBase`** y `ForzarHttps`. Lo más grande del cambio: ~25 navegaciones y enlaces absolutos (`/pipelines`, `/login`, `/`)
  sacaban a la persona del prefijo; pasaron a relativos. `<base href>` sale de la config, no del host del pedido.
- `docs/Despliegue-AppHub.md` (alta en AppHub, clave, `PathBase`, qué tiene que cumplir el proxy).
- 41 pruebas nuevas (297 en total, 0 fallas).

**Verificado en navegador** con el Admin real y un proxy de prueba que agrega el encabezado firmado a cada pedido,
websocket incluido: token correcto → entra solo, con el correo y sin «Salir», interactivo y bajo `/datapipeline/`; firma mala →
login con aviso; sin encabezado → login normal; `/login` con identidad válida → inicio.

**Revisión:** `docs/Revisiones/00157-review-1.md` (ronda 1 de 2). **La hizo un subagente leyendo el diff, no Codex**
(el sandbox de Codex no arranca). 8 hallazgos: se corrigieron el 1 (vida del token acotada), el 4 (carrera en
`InicializarAsync`), el 8 (comentarios por bloque) y parte del 5 (correo con caracteres de control); el 3, el 6 y el 7 no
eran un problema; el 2 es un riesgo aceptado (ver abajo). No hay ronda 2.

**Pendiente del dueño:**
- **Probar contra el AppHub real.** Dos supuestos sin confirmar: que agregue `X-AppHub-Identidad` también a la negociación y
  al websocket de `/_blazor`, y que conserve el `Host` original (Blazor arma las redirecciones con él).
- **La sesión sobrevive a la revocación:** el circuito lee la identidad una vez. Si en AppHub le quitan el acceso a alguien
  con la pestaña abierta, sigue adentro hasta recargar. Un tope de duración es decisión suya.
- Alta de `datapipeline` en `Apps[]` de AppHub con la misma clave (no se tocó el repo de AppHub).
- `main` quedó 2 commits adelante de `origin/main` (perfiles de publicación de la 00155) y nada de esto está pusheado.

## TAREA 00155 — 01/11/2026

**Pedido (textual):** «ahi estuve probando y me dio errores momento de actualizar un pipeline para
recargar la pagina pero no me dijo cual era el problema. ahi me di cuenta que faltarian unos logs
para los errores. lo podes agregar»

**Diseño previo:** no aplica. No cambia el flujo ni agrega pantallas; es manejo de errores y logging.

**El sintoma y las tres causas.** Un error al guardar recargaba la página sin decir nada y no había
dónde ir a buscarlo. Mirando el camino aparecieron tres huecos encadenados:

1. **No había log a archivo en ninguna parte.** Los errores sólo existían en la consola de la ventana
   que levanta el servicio: si nadie la estaba mirando, se perdían.
2. **La API devolvía 500 sin cuerpo** ante una excepción no atrapada. El Admin veía «no fue 2xx».
3. **Los servicios del Admin devolvían `bool`** y tiraban el motivo que la API sí mandaba. La
   pantalla mostraba «revisa la API o el nombre duplicado»: dos causas posibles entre muchas, ninguna
   confirmada, y en el caso real ninguna de las dos.

**Ejecutado (ítem 1):**

- `DataPipeline.Core/Logging/ArchivoLogger.cs`: proveedor propio, sin dependencias nuevas más que las
  Abstractions de logging. Un archivo por día, rota a los 10 MB, borra a los 30 días. **Nunca tira**:
  un logger que revienta se lleva puesta la aplicación que se quería diagnosticar, que es el peor
  momento posible. Si no puede escribir, avisa una vez por consola y sigue.
- `DataPipeline.Api/Middleware/ManejadorDeErrores.cs`: loguea la excepción completa con su stack y
  responde `{ error, traceId }`. **El traceId es la pieza que faltaba**: es el mismo número en la
  pantalla y en el archivo, así que deja de hacer falta adivinar cuál de los errores del log es el
  que se acaba de ver. Las causas que el usuario puede arreglar se nombran por el texto del error y
  no por el tipo, para no atar el middleware al proveedor de base.
- `DataPipeline.Admin/Services/ErroresApi.cs`: lee el motivo venga como venga — formato propio,
  ProblemDetails de ASP.NET (donde lo útil está en `errors`, no en `title`), texto pelado, o nada.
  Los servicios devuelven `ResultadoApi` y las pantallas muestran el texto real.
- EF a `Warning`: a `Information` escribe cada SQL y el archivo se vuelve ilegible justo cuando hace
  falta leerlo.

**Verificado sobre el paquete publicado**, no sólo en pruebas: se forzó un nombre duplicado real, la
respuesta fue «Ya existe un registro con ese nombre» con su traceId, y ese mismo traceId apareció en
`api\logs\dp-api-20261101.log` con el `UNIQUE constraint failed: Origenes.Nombre` y el stack completo.

**256 pruebas** (+23: 17 del logger y la lectura de errores, 6 de los mensajes al usuario).


## TAREA 00154 — 30-31/10/2026

**Pedido (textual):** «deberiamos tomar desde d:\sistemas\consultasgenericas\api todos los tipos de
conexion que tiene. porque tengo para probar casos reales pero con oracle.» Y después, el giro que
cambió el alcance: «y si usamos la api de consultas genericas, anotalo que de ahora en adelante se
llama datahub para futuras referencias. se podria para no hacer un gran trabajo ahora??», más «si te
paso la url de la api. vos me traerias todos los endpoints que estan definidos en datahub para
seleccionar y como hacemos para poner los parametros». El origen se llama **DataHub CompLook** por
pedido explícito, para dejar libre el nombre de un conector REST genérico futuro.

**Numeración — se renumeró dos veces.** Empezó como **00151**: lo tomó ConciliadorContable
mientras se diseñaba, y se pasó a **00152** sin código escrito. Al cerrar, el 00152 también estaba
tomado (ConsultasGenericas) y el 00153 igual (ArcaAutomatizacion), así que quedó en **00154**. El
contenido —comentarios de versionado, carpeta de diseño, documentos— dice 00154. **Los mensajes de
los cuatro commits de código siguen diciendo 00152**, por el mismo criterio que se usó en el choque
00014/00015: no se reescriben commits ya hechos. El quinto commit es el de la renumeración.

**Diseño:** `docs/Diseño/00154-origen-datahub/` (guía, flujograma y 7 maquetas).

**Por qué no se portaron los drivers:** los contratos son incompatibles. DataPipeline lee en
streaming (`IAsyncEnumerable<RecordBatch>`), DataHub materializa (`QueryResultDto`). No son
adaptables uno al otro: había que reescribir siete ejecutores, no copiarlos. Consumir la API de
DataHub alcanza a Oracle, MySQL, MariaDB, PostgreSQL y SQL Server con un solo conector.

**Ejecutado (4 ítems, 4 commits):**

- **Ítem 1 — El origen.** `TipoDataSource.DataHubCompLook`; `DataHubLector` recorre el array `rows`
  con `Utf8JsonReader` sobre el stream, sin materializarlo; `DataHubSource` arma la llamada y
  autentica. Conversión de valores por el esquema declarado, siempre con `InvariantCulture`.
  `PipelineHydrator` pasa de `if SqlServer … else archivo` a `switch` exhaustivo.
- **Ítem 2 — El catálogo.** `IDataHubCatalogo` + `GET /api/datasources/{id}/endpoints`. Filtra los
  inactivos y mapea ruta, método, motor, parámetros declarados y `ResultSchemaJson`.
- **Ítem 3 — Probar y traer columnas.** Las columnas salen del esquema que DataHub ya guardó, sin
  ejecutar nada. `DataSourcesController.Probar`/`.Columnas` pasan a `switch` por tipo.
- **Ítem 4 — Admin.** Diálogo del origen con las dos credenciales rotuladas; Paso 1 con combo de
  endpoints, parámetros declarados y el watermark como no editable.

**Tres bugs encontrados al construir, no al planificar:**

1. **Fila partida entre dos lecturas del stream.** Al detectar que una fila no llegó entera, el
   `StartObject` ya estaba consumido: esa fila se perdía **en silencio** — menos filas y ningún
   error. Se retrocede el lector. Probado con cortes cada 1, 7, 13, 64 y 4096 bytes.
2. **La regla R1 estaba mal.** Decía "mandar siempre el watermark". `ArmarParametros` de DataHub
   mete en el comando **todos** los parámetros que recibe, estén o no en el SQL, así que uno de más
   rompe la consulta (en Oracle, `ORA-01036`). Se manda exactamente lo que el endpoint declara.
3. **`Editor.razor:434` tenía `_ => "SqlServerSource"`.** Un origen DataHub se guardaba con el
   conector equivocado: no fallaba al guardar, fallaba al correr con un error de conexión que no
   tenía nada que ver con la causa.

**Verificación en el navegador** (lo que en tareas anteriores quedaba como «sin ver la pantalla»):
se levantó un doble de DataHub que habla su mismo protocolo, incluido el reparto de credenciales
(el admin no puede leer datos; el ApiUser no puede listar el catálogo). Se recorrió el circuito
completo desde el Admin: alta del origen → Probar → catálogo → endpoint → parámetros → columnas →
guardar → correr. **Primera corrida: 3 filas, con `watermark=` vacío en la URL. Segunda: 0 filas,
con el watermark ya avanzado a `2026-10-30T16:44:00`.** Probando la pantalla apareció un cuarto
hallazgo que las pruebas no veían: al editar un pipeline guardado no se mostraba qué endpoint tenía
configurado. Corregido.

**Lo que NO se probó:** un DataHub real y una base Oracle. Todo lo verificado es contra el doble.

**Revisión de Codex (ronda 1):** `docs/Revisiones/00154-review-1.md`. **Cinco hallazgos, los cinco
corregidos.** Los dos que más importan: el endpoint se identificaba sólo por la ruta, cuando DataHub
lo busca por ruta **y** método (la misma ruta puede estar publicada como GET y como POST); y editar
un pipeline guardado le cambiaba el método a GET y le borraba el esquema, en silencio, porque al
cargar no se restauraban esos dos campos y al guardar se reescribían igual. Codex no encontró nada
en seguridad ni en el parseo en streaming. 233 pruebas (+5 de regresión).

Para correrla hubo que sortear el sandbox: se le pegó la guía y los 144 KB del diff en el prompt, así
no necesitó terminal, ni GitHub, ni aflojar permisos.

**Reparto con Codex (diseño):** no se pudo usar. El sandbox de Codex en esta máquina se bloquea a sí mismo
(levanta sus propios helpers MCP, que toman los binarios del runtime, y después el paso de permisos
falla sobre esos mismos archivos). Se diagnosticó y se arregló una causa real —la carpeta del repo
había quedado **sin dueño**, y sin ser dueño no se pueden escribir ACLs—, pero quedó el bloqueo que
Codex se produce solo. **El flujograma y las maquetas los hizo Claude**, y la revisión de la Etapa 4
quedó pendiente: decide el dueño del pedido.

**Estado:** 228 pruebas verdes, build Release sin errores. 4 commits en la rama
`tarea-00154-origen-datahub`, sin mergear ni pushear.

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
- **Revision final de rama completa** (TAREA 00015, posterior al cierre de Task 11): la Task 11
  verifico build y tests, pero no probo XLSX con celdas numericas/fecha REALES bajo una cultura
  distinta de en-US — todos los tests de XLSX existentes usaban celdas de texto. Esa revision de
  rama completa encontro 1 hallazgo Critico y 3 Importantes que ninguna revision por task habia
  detectado, corregidos en el commit `598464c`:
  - **Critico** — `XlsxSource.LeerFilasAsync` convertia cada celda con `.Value.ToString()`, que en
    ClosedXML formatea `Number`/`DateTime` con la cultura de la maquina (ej. es-AR: `10.5` -> texto
    `"10,5"`, una fecha -> `dd/MM/yyyy`). `TipoValorConverter.Convertir` reparsea ese string con
    `InvariantCulture`, que lee `"10,5"` como `105` (silencioso, corrompe el valor Y el filtro de
    watermark) y hace explotar cualquier fecha con dia > 12 (`FormatException`). Fix: se extrae el
    valor NATIVO de la celda segun `XLCellValue.Type` (`XLDataType.Number` -> `GetNumber()` (double),
    `.DateTime` -> `GetDateTime()`, `.Boolean` -> `GetBoolean()`, `.TimeSpan` -> `GetTimeSpan()`,
    resto -> `GetText()`), que es lo que el spec original pedia y evita el paso por un string
    localizado. Verificado RED/GREEN: revirtiendo el fix, los 2 tests nuevos (`Bajo_cultura_es_AR_*`)
    fallan exactamente como se predijo (105 en vez de filtrar, y `FormatException` en la fecha);
    con el fix, pasan.
  - **Importante** — un CSV vacio (0 bytes) hacia que `csv.Read()` devolviera `false` y
    `csv.ReadHeader()` tirara `ReaderException`; como `FileSourceBase` solo mueve un archivo a
    `\procesados` despues de una lectura limpia, el archivo vacio quedaba trabado en la carpeta de
    entrada, fallando en cada corrida siguiente. Fix: mismo guard `if (!csv.Read()) ...` que ya
    tenia la rama sin encabezado, aplicado tambien a la rama con encabezado (`LeerEncabezado` y
    `LeerFilasAsync`).
  - **Importante** — una celda en blanco (`""`) en la columna watermark (CSV con un campo vacio, o
    Excel con una celda vacia en una columna por lo demas poblada) tumbaba toda la corrida:
    `FiltrarYTipar` solo chequeaba `crudo is not null`, no longitud vacia, y
    `TipoValorConverter.Convertir("", ...)` tira `FormatException`. Fix: se trata `""` igual que
    `null` (fila emitida sin tipar/filtrar ese valor).
  - **Importante** — el Editor del Admin dejaba guardar un pipeline con columna watermark elegida
    pero sin su tipo, lo que hacia que `FileSourceBase` cayera al default `"string"` (comparacion
    lexicografica silenciosa) — el mismo bug de fondo que el diseño de tipado de watermark existe
    para evitar. Fix: `Guardar()` valida que, para un origen de archivo con columna watermark no
    vacia, el tipo tambien este elegido. Sin test automatizado (Admin es build-only por convencion
    de este repo); verificado por build limpio.

  Tests nuevos: 4 (2 en `XlsxSourceTests`, 1 en `CsvSourceTests`, 1 en `FileSourceBaseTests`).
  Suite final: **103 tests verdes** (15 Persistence + 66 Core/Connectors/Engine + 22 Api), build
  0 warnings/0 errores. Reporte detallado:
  `.superpowers/sdd/task-11-final-review-fix-report.md`.

**Fuera de alcance (explicito, decidido con el usuario):** MySQL/MariaDB/Oracle/RestApi (existen en
el proyecto madre ConsultasGenericas pero no se pidieron aca); upload manual de archivos por el
Admin (se eligio carpeta de red fija); deteccion automatica de si un archivo es dump acumulativo o
delta (el mecanismo de mover a `\procesados` lo resuelve sin necesitar saberlo).

**Pendiente del dueño:** verificacion manual end-to-end (crear un `DataSource` Csv y uno Xlsx, armar
un pipeline contra cada uno, correrlo y confirmar que el archivo se mueve a `\procesados` y que una
segunda corrida sin archivos nuevos no reprocesa nada) — el Admin es build-only por convencion de
este repo, sin tests automatizados de UI.

---

## TAREA 00038 — 12-17/09/2026

**Pedido (numerado y confirmado con el usuario antes de planificar):**

1. Entidad `Envio`: push del resultado de un pipeline a una tabla nuestra en un SQL Server remoto.
   Mueve columnas, no transforma.
2. Encadenamiento pull → push por bandera `Pendiente`: el pipeline la prende al terminar OK, el tick
   de 60 s la consume, reintenta si falla.
3. Pantallas del Admin: lista de envios, alta/edicion con probar conexion, y el estado de los envios
   en el historial de corridas.

El pedido original fue "retomar la idea de hacer push desde pipeline". En el brainstorm se descubrio
que **no es el sink REST de la Fase 5** que decia `ANALISIS-MIDDLEWARE.md:467`: el destino real es
otra base SQL Server en otro servidor. Mismo concepto de entrega, transporte ADO.NET en vez de HTTP.

**Diseño previo:** `docs/Diseño/00038-envio-sql/` — flujograma, 5 maquetas y `envio-sql.md`
(actualizado al terminar con lo que se resolvio al construir).
**Plan:** `docs/superpowers/plans/2026-09-12-datapipeline-envio-sql.md` (12 tasks).

**Decisiones que fijaron el alcance:** la tabla destino es nuestra alojada en el servidor de ellos
(sin mapeo de columnas ni constraints ajenas); el push mueve columnas y no transforma (eso queda en
el `ColumnMapTransform` del pipeline); etapa de entrega separada, que preserva el desacople
extraccion/entrega de la seccion 13 del analisis; y el envio no tiene reloj propio sino que se
dispara por bandera `Pendiente` — asi el disparo y el reintento son la misma operacion sobre el tick
de 60 s que ya existia, sin agregar un `TipoProgramacion` nuevo.

**El cursor salio gratis:** `_creado` ya se refresca en el upsert de `SqliteDataStore` (linea 78), o
sea que no es "cuando nacio la fila" sino "cuando se escribio por ultima vez". El push lee
`WHERE _creado > UltimoCreado` **sin tocar `_procesado`**, asi que no le pisa filas a ningun
consumidor PULL: esquiva la limitacion del ack global en vez de agravarla.

**Ejecutado (12 tasks, inline con `superpowers:executing-plans`, un commit por task):** entidad y
esquema (DDL idempotente, sin migracion EF) → `IEnvioStore` con toma anti-solape → lectura por cursor
→ destino SQL Server (`IF EXISTS...UPDATE ELSE INSERT`, sin `MERGE`) → backoff → `EjecutorEnvios` →
enganche en el scheduler y en la corrida manual → API → las tres pantallas del Admin.

**Siete correcciones al plan, encontradas al ejecutarlo contra el codigo real:**

1. **Agujero de seguridad:** el `EnviosController` del plan llevaba `[Authorize]` **sin**
   `Roles = RolAdmin`, a diferencia de Pipelines y DataSources. El usuario **pull** (el consumidor
   externo) podia crear envios apuntados a cualquier servidor SQL usando las credenciales ya
   guardadas en los origenes. Verificado en los dos sentidos: sin el rol, pull recibe 200 OK.
2. **Bug de paginacion (perdida silenciosa de datos):** `LeerDesdeAsync` paginaba por `OFFSET`. Una
   corrida manual (`POST api/run`) corre en paralelo con el tick; si reescribe en `Reemplazar` una
   fila ya leida, esa fila salta al final del orden y corre todas las posiciones siguientes — la que
   cae en el hueco no se lee nunca y el cursor avanza igual. Reproducido con el mismo SQL contra
   SQLite antes del fix (se perdia la fila 3 de 5). Se paso a **keyset** `(_creado, _key)`, con test
   de regresion.
3. **Estado viejo en los tests del store:** `GetByIdAsync` trackeaba y casi todo se escribe con
   `ExecuteUpdateAsync`, que pasa por fuera del change tracker; EF habria devuelto la instancia
   cacheada con los valores de antes. Lecturas `AsNoTracking` + `ChangeTracker.Clear()` al guardar.
4. **Bug de cultura:** `_creado` vuelve de SQLite como texto y el plan lo convertia con
   `Convert.ToDateTime` (dependiente de la cultura) — el mismo bug que ya se habia corregido en el
   commit `598464c`. Se parsea con `InvariantCulture` y viaja al destino como `DateTime`.
5. **`Partes()` destruia identificadores validos:** sacaba los corchetes con `Replace`, lo que rompe
   un `]` legitimo del nombre y parte mal `[mi.esquema].[t]`. Se escribio un parser que respeta las
   reglas de T-SQL, y ademas **rechaza nombres de tres partes** para no escribir en otra base.
6. **Off-by-one en los reintentos** (detectado en el self-review del plan, antes de ejecutar):
   `MaxIntentos = 5` con backoff 1-2-4-8-16 daba en realidad seis intentos, y el badge "5/5" nunca
   coincidia con el estado Fallido. Quedo en 5 fallos totales con esperas de 1, 2, 4 y 8 min; se
   regeneraron el flujograma y la maqueta `04`.
7. **Validacion del nombre de tabla al guardar** (no estaba en el plan): sin eso, un error de tipeo
   se aceptaba, fallaba cinco veces en runtime y el envio se deshabilitaba solo — el administrador se
   enteraba quince minutos despues.

Ademas, `EjecutorEnvios` recibe `filasPorLote` por constructor: con el valor de produccion (5.000) la
rama que pide el lote siguiente por keyset —justo el codigo del fix 2— no se ejercitaria en ningun
test. Y hay un test de DI explicito (`EnvioDiTests`) porque el hosted service resuelve `EjecutorEnvios`
**dentro** del tick y traga la excepcion logueandola: un DI mal armado no romperia el arranque ni
ningun otro test, y en produccion serian errores en el log cada 60 s sin que saliera un solo envio.

**Suite final: 186 tests verdes** (34 Persistence + 119 Core/Connectors/Engine + 33 Api; 103 de base
+ 83 nuevos), build `--no-incremental` con 0 warnings y 0 errores.

**Fuera de alcance (explicito):** destino con esquema ajeno (mapeo de columnas, upsert por clave
compuesta de ellos, politica de filas rechazadas); destinos que no sean SQL Server; arreglar el ack
global (`_procesado` sin identidad de consumidor); renombrar `DataSource` a "Conexion".

**Pendiente del dueño:**

- **Confirmar que el DBA otorga `CREATE TABLE`** en la base remota. Toda la decision 1 del diseño
  cuelga de eso; si la respuesta es no, hay que rediseñar hacia "la tabla es de ellos" (~50-60% mas
  de trabajo, detallado en §7 del diseño).
- **Probar la escritura real contra un SQL Server.** No hay servidor en el entorno de desarrollo:
  `SqlServerDestinoSql` esta testeado puro y `SqlServerDestino` es una cascara fina, pero la ruta
  completa no se ejercito en vivo. Mismo criterio que TAREA 00026.
- **Verificacion visual de las tres pantallas** contra las maquetas (el Admin es build-only por
  convencion de este repo, y la verificacion pasa por el login: Claude no tipea contraseñas).
