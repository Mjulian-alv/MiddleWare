# DataPipeline — Instructivo del proyecto (estado al 2026-06-22)

Guia practica de que es el proyecto, como esta organizado, que hace cada pieza (incluido el CLI),
como se ejecuta y como se prueba. Escrito para que cualquiera entienda el sistema sin leer el codigo.

> Documento de referencia tecnica/arquitectura: `ANALISIS-MIDDLEWARE.md`.
> Estado y decisiones pendientes de Fase 4: `docs/superpowers/fase4-roadmap-decisiones.md`.

---

## 1. Que es DataPipeline

Es un **middleware de integracion de datos** propio (solucion .NET 10 separada e independiente de
`ConsultasGenericas`). Su trabajo:

> **Leer** de un origen (hoy SQL Server) → **transformar** los datos (renombrar/castear/calcular/
> filtrar columnas) → **dejarlos** en una **tabla de serving** intermedia → exponerlos por una
> **API PULL autenticada** para que otros sistemas los consuman.

Es **PULL**: el middleware no empuja a destino; deja los datos servidos y el consumidor los retira
por HTTP (`GET`) y confirma lo que tomo (`ack`). El destino final lo decide el consumidor.

Todo corre por **streaming y por lotes** (no materializa todo en memoria) y los pipelines se definen
de forma **declarativa** (sin escribir codigo): origen + transformaciones + tabla destino + horario.

---

## 2. Arquitectura: los proyectos y para que sirve cada uno

La solucion (`DataPipeline.slnx`) tiene 7 proyectos de produccion + 3 de tests:

```
DataPipeline.Core         ← contratos y modelos puros (sin dependencias externas)
DataPipeline.Connectors   ← implementaciones concretas (SQL Server, SQLite, transforms, cifrado)
DataPipeline.Engine       ← el motor: arma y corre la tuberia; scheduler; hidratacion
DataPipeline.Persistence  ← almacen A (EF Core/SQLite): definiciones + historial + datasources
DataPipeline.Api          ← host web: endpoints HTTP, auth, scheduler de fondo (deployable)
DataPipeline.Admin        ← UI de administracion (Blazor Server + Syncfusion)
DataPipeline.Cli          ← utilitario de linea de comandos (ver seccion 4)
tests/*                   ← DataPipeline.Tests, .Persistence.Tests, .Api.Tests (58 tests verdes)
```

**Como se relacionan:** `Core` define las interfaces; `Connectors` y `Persistence` las implementan;
`Engine` orquesta usando esas interfaces. Despues hay **tres "caras" del mismo motor**:
- **Api** → cara HTTP (para consumidores y para el Admin).
- **Admin** → cara visual (para administrar sin tocar JSON).
- **Cli** → cara de linea de comandos (para automatizar/probar/bootstrap).

### Detalle por proyecto

- **Core** — Modelos de dominio (`PipelineDefinition`, `StepDefinition`, `PipelineRun`, `DataSource`,
  enums `ModoEscritura`/`TipoProgramacion`/`TipoPaso`) y **contratos** (`ISource`, `ITransform`,
  `IDataStore`, `ISourceFactory`/`ITransformFactory`, `IPipelineStore`, `IDataSourceStore`,
  `IHistorialCorridas`, `IEncryptionService`, `IConnectionTester`). **No referencia nada externo** —
  es la "forma" del sistema, barata de referenciar.
- **Connectors** — Implementaciones: `SqlServerSource` (lee de SQL Server en streaming),
  `SqliteDataStore` (serving con ciclo de procesado), `ColumnMapTransform` (transform declarativo con
  NCalc), `SourceFactory`/`TransformFactory`, `EncryptionService` (AES), `ConnectionTester`.
- **Engine** — `PipelineRunner` (compone Source → Transform(s) → DataStore por lotes),
  `PipelineHydrator` (resuelve `dataSourceId` → connection string antes de correr),
  `ScheduleCalculator` (proxima corrida segun horario), `EjecutorProgramado` (corre los vencidos).
- **Persistence** — `MetadataDbContext` (EF Core sobre SQLite, "almacen A") + repos
  `EfPipelineStore`/`EfHistorialCorridas`/`EfDataSourceStore` + migraciones. Guarda definiciones de
  pipeline, historial de corridas y datasources (secretos cifrados).
- **Api** — Host ASP.NET Core. Endpoints (seccion 5), Basic Auth con dos roles (`pull`/`admin`),
  y un **scheduler de fondo** (`PipelineSchedulerHostedService`) que cada ~60s corre los pipelines
  vencidos. Es el proyecto **deployable**.
- **Admin** — Blazor Server + Syncfusion. UI para gestionar datasources y pipelines, dispararlos y
  ver el historial, **sin escribir JSON**. Habla con la Api por HTTP (nunca toca la BD directo).
- **Cli** — Ver seccion 4.

---

## 3. El flujo de datos (como funciona, end-to-end)

```
[SQL Server origen]
      │  (SqlServerSource, streaming por lotes)
      ▼
[Transform(s): rename / cast / computed / filter]   (ColumnMapTransform, NCalc)
      │
      ▼
[Serving SQLite]  ← cada fila lleva columnas de control: _key, _creado, _procesado
      │
      ▼
GET /api/data/{pipeline}   ← el consumidor retira SOLO lo no procesado
      │
      ▼
POST /api/data/{pipeline}/procesar  ← el consumidor confirma (ack) las keys que tomo
```

**Ciclo de procesado (Fase 4A):** cada fila del serving tiene:
- `_key` — identidad de la fila (de las "columnas clave" que elegis en el pipeline).
- `_creado` — cuando el middleware la escribio/actualizo.
- `_procesado` — 0 hasta que el consumidor la confirma; entonces pasa a 1.

Segun el **modo de escritura** del pipeline:
- **Agregar** — cada corrida inserta lo nuevo; al inicio de la siguiente, **borra las ya procesadas**
  (para no acumular).
- **Reemplazar** — **upsert por `_key`**: si la key ya existe la actualiza (`_creado=now`,
  `_procesado` vuelve a 0); si es nueva la inserta; las que ya no vienen quedan.

**Programacion:** un pipeline puede ser `BajoDemanda` (se corre a mano) o `Diaria`/`Semanal`/
`Mensual` (el scheduler de la Api lo corre solo a la hora configurada).

---

## 4. El proyecto CLI — que es y para que sirve (lo que no se entendia)

`DataPipeline.Cli` es una **aplicacion de consola** que maneja **el mismo motor** que la Api
(referencia Core/Connectors/Engine/Persistence), pero **desde la linea de comandos, sin levantar la
Api ni el Admin**. Es una herramienta de **desarrollo/operaciones/automatizacion**.

**Por que existe:**
- Nacio como el **entregable de Fase 1**: probar que el motor (origen → transform → store) funciona
  end-to-end **antes** de que existieran la Api, el scheduler o la UI.
- Sigue siendo util para: **automatizar** (scripts, tareas programadas del SO, CI), **bootstrap**
  (cargar pipelines sin la UI), **pruebas rapidas** y **diagnostico** (correr un pipeline puntual y
  ver el resultado en una consola).

**Tiene 3 modos (subcomandos):**

1. **Correr un pipeline ad-hoc desde un JSON** (modo por defecto):
   ```bash
   dotnet run --project DataPipeline.Cli -- <pipeline.json> <salida.sqlite> [claveBase64]
   ```
   Carga un pipeline definido en un archivo JSON y **lo corre una vez**, escribiendo a una SQLite.
   No usa la BD de metadatos ni el scheduler: es la forma mas directa de probar un pipeline.
   Imprime `Run <id> -> Success=... Read=... Written=... Error=...`.

2. **Importar una definicion al almacen A**:
   ```bash
   dotnet run --project DataPipeline.Cli -- import <pipeline.json> <metadata.db>
   ```
   Registra (upsert) un pipeline definido en JSON dentro de la BD de metadatos, calculando su
   proxima corrida. Equivale a "crear el pipeline" pero sin la UI del Admin — util para cargar
   pipelines de forma programatica o versionados en archivos.

3. **Sembrar datos de demo** (agregado en Fase 4A, para probar sin SQL Server):
   ```bash
   dotnet run --project DataPipeline.Cli -- seed-demo <metadata.db> <data.db>
   ```
   Registra un pipeline `demo` y siembra 5 filas de ejemplo en el serving (ya con columnas de
   control). Sirve para probar el flujo PULL (GET/ack) sin tener un SQL Server con datos.

> En resumen: **la Api es el motor "como servicio" (HTTP + scheduler + UI detras); el CLI es el motor
> "como herramienta" (un comando, una corrida)**. Misma maquinaria, distinta cara. Para produccion se
> usa la Api + Admin; el CLI es para automatizar, bootstrapear y probar.

---

## 5. La API — endpoints

Base: `https://localhost:7221` (y `http://localhost:5127`). **Basic Auth** con dos credenciales:
- `pull` / `pull123` — rol PULL: **solo lectura de datos** servidos.
- `admin` / `admin123` — rol ADMIN: **gestion** (pipelines, datasources).

(Ambas son **defaults de DEV**; cambiarlas en produccion — ver seccion 8.)

| Metodo | Ruta | Rol | Que hace |
|--------|------|-----|----------|
| GET  | `/api/data/{nombre}?page=&pageSize=&incluirProcesados=` | pull | Filas del serving del pipeline. Por defecto **solo no procesadas**. |
| POST | `/api/data/{nombre}/procesar` body `{ "keys": [...] }` | pull | **Ack**: marca esas filas como procesadas. Devuelve `{ marcadas }`. |
| GET  | `/api/pipelines` · `/api/pipelines/{id}` | admin | Lista / ve un pipeline. |
| POST | `/api/pipelines` | admin | Crea o edita (upsert) un pipeline. |
| DELETE | `/api/pipelines/{id}` | admin | Borra un pipeline. |
| GET  | `/api/datasources` · `/api/datasources/{id}` | admin | Lista / ve un datasource (sin secretos). |
| POST | `/api/datasources` | admin | Crea/edita un datasource (cifra secretos; vacio = conservar). |
| DELETE | `/api/datasources/{id}` | admin | Borra un datasource. |
| POST | `/api/datasources/{id}/probar` | admin | Prueba la conexion (fallo amable si no conecta). |
| POST | `/api/run/{id}` | autenticado | Corre un pipeline on-demand; devuelve el resultado. |
| GET  | `/api/runs?max=` | autenticado | Historial de corridas. |

---

## 6. El Admin — pantallas

Base: el Admin es un sitio Blazor aparte (puerto propio segun su `launchSettings`). Pantallas:

| Ruta | Que hace |
|------|----------|
| `/login` | Login local del panel (usuario `dpAdmin`, password que definis en el 1er ingreso). |
| `/` | Inicio. |
| `/datasources` | CRUD de origenes (grilla + form modal + "probar conexion"). |
| `/pipelines` | Lista de pipelines; "Nuevo", "Editar" y "Correr ahora" por fila. |
| `/pipelines/nuevo` · `/pipelines/editar/{id}` | Editor visual: generales + programacion + origen (datasource + SQL) + **transformaciones en cadena** (rename/cast/computed/filter) + columnas clave. Genera el JSON sin tocarlo. |
| `/runs` | Historial de corridas (estado, filas, mensaje). |

> El Admin tiene **dos capas de auth**: (1) login local del panel (`AdminAuthService`), y (2) llama a
> la Api con la credencial `admin` (config `ApiAuth` del Admin).

---

## 7. Como ejecutar todo

Requisitos: .NET 10 SDK. Desde la raiz `D:\Sistemas\MiddleWare`.

```bash
# Compilar y testear toda la solucion
dotnet build
dotnet test

# Correr la API (deployable). Crea metadata.db y data.db en su carpeta.
dotnet run --project DataPipeline.Api          # https://localhost:7221

# Correr el Admin (UI). Necesita la API arriba y la clave Syncfusion (ver seccion 8).
dotnet run --project DataPipeline.Admin

# Usar el CLI (ver seccion 4)
dotnet run --project DataPipeline.Cli -- seed-demo DataPipeline.Api/metadata.db DataPipeline.Api/data.db
dotnet run --project DataPipeline.Cli -- import mi-pipeline.json DataPipeline.Api/metadata.db
dotnet run --project DataPipeline.Cli -- mi-pipeline.json salida.db
```

---

## 8. Configuracion

**API** (`DataPipeline.Api/appsettings.json`):
- `ConnectionStrings:Metadata` — BD de definiciones/historial/datasources (default `metadata.db`).
- `ConnectionStrings:Data` — BD de **serving** (default `data.db`). Aca aterrizan los datos.
- `Encryption:Key` — clave AES base64 (32 bytes) para cifrar secretos de los datasources. **Cambiar
  en produccion** (el default son 32 ceros).
- `PullAuth` / `AdminAuth` — usuario + `PasswordHash` (BCrypt) de cada rol. Hoy: `pull123` / `admin123`
  (DEV). En produccion, generar hashes nuevos y reemplazarlos.

**Admin** (`DataPipeline.Admin/appsettings.json`):
- `SyncfusionLicenseKey` — clave de licencia Syncfusion. **No commitear** (ponela local o en
  `appsettings.Development.json` / user-secrets / variable de entorno).
- `ApiBaseUrl` — URL de la API (`https://localhost:7221`).
- `ApiAuth:Username`/`Password` — credencial con la que el Admin llama a la API (`admin`/`admin123`).
- `ApiAcceptAnyServerCertificate` — `true` para aceptar el cert dev self-signed (solo dev).

> Secreto de runtime: `DataPipeline.Admin/admin-credentials.json` (password local del panel hasheada)
> esta **gitignoreado** — no se commitea.

---

## 9. Como probar el flujo PULL sin SQL Server (demo)

```bash
# 1) (arranque limpio) borrar DBs viejas para evitar drift de esquema
rm -f DataPipeline.Api/metadata.db DataPipeline.Api/data.db

# 2) sembrar el pipeline 'demo' + 5 filas en el serving
dotnet run --project DataPipeline.Cli -- seed-demo DataPipeline.Api/metadata.db DataPipeline.Api/data.db

# 3) arrancar la API
dotnet run --project DataPipeline.Api

# 4) ejercer el ciclo (otra terminal)
curl -k -u pull:pull123 "https://localhost:7221/api/data/demo"                 # 5 filas no procesadas
curl -k -u pull:pull123 -X POST "https://localhost:7221/api/data/demo/procesar" \
     -H "Content-Type: application/json" -d "{\"keys\":[\"1\",\"2\"]}"          # ack de 1 y 2
curl -k -u pull:pull123 "https://localhost:7221/api/data/demo"                 # quedan 3,4,5
curl -k -u pull:pull123 "https://localhost:7221/api/data/demo?incluirProcesados=true"  # las 5
```

`_key` = el valor de la columna clave (en la demo, `id`), por eso se ackea con `"1"`, `"2"`.

---

## 10. Convenciones y workflow del repo

- **Idioma (Opcion 2):** enums/propiedades de dominio en castellano; interfaces/tipos de runtime/
  servicios/metodos en ingles (.NET); variables locales y comentarios en espanol. **Sin tildes ni `ñ`
  en identificadores** (si en comentarios/strings).
- **Streaming obligatorio:** los transforms procesan lote a lote, nunca materializan todo.
- **Core sin dependencias externas** (solo BCL).
- **Build pristine** (0 warnings) — verificar con `dotnet build --no-incremental`.
- **TDD** donde es testeable (motor, persistencia, API con SQLite real + WebApplicationFactory). La
  UI del Admin se verifica con build + prueba manual.
- **Git:** se commitea directo a `main`, un commit por tarea (no hay remoto configurado). **No usar
  `git add -A`** (riesgo de colar secretos/locales); stagear rutas explicitas.
- **Plan-driven:** cada fase/rebanada pasa por brainstorming → spec (`docs/superpowers/specs/`) →
  plan (`docs/superpowers/plans/`) → ejecucion. Los ledgers de avance estan en `.superpowers/sdd/`.

---

## 11. Estado y que falta

**Completo:** Fase 1 (cimientos), Fase 2 (motor usable + PULL), Fase 3 (UI Admin: 3A-3E),
**Fase 4A** (serving con ciclo de procesado). Build pristine, **58 tests verdes**.

**Pendiente (Fase 4):** reintentos (Polly), watermark del origen, dead-letter, encadenamiento
(`RunAfter`), 2do source heterogeneo (MySQL/Oracle/Excel/RestApi), y `SqlServerDataStore` configurable
(diferido). Cada una con sus decisiones de diseno en `docs/superpowers/fase4-roadmap-decisiones.md`.
Orden sugerido: **4C → 4B → 4E → 4F → 4G**.

**Limitaciones actuales a tener presentes:**
- El **unico origen** que ejecuta es SQL Server (los demas tipos son Fase 4G).
- El **destino** es siempre la SQLite de serving (intermedia); el destino final lo hace el consumidor.
- `ModoEscritura.Combinar` no se ofrece (el store lo rechaza); el upsert se logra con `Reemplazar`.
- El editor no expone `RunAfter` todavia (su ejecucion es Fase 4F).
