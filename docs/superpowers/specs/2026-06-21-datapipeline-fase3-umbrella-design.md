# DataPipeline — Fase 3 (UI Admin) — Spec Paraguas

> **Naturaleza:** documento paraguas. Define el alcance, la descomposicion en rebanadas, el
> orden y los contratos transversales de la Fase 3. **Cada rebanada (3A..3E) tendra su propio
> ciclo** brainstorming/spec -> writing-plans -> ejecucion. Este documento NO es el plan de
> implementacion de ninguna rebanada en particular.

**Fecha:** 2026-06-21
**Estado:** aprobado (secciones 1-3 validadas con el usuario).
**Precede a:** specs y planes individuales de 3A, 3B, 3C, 3D, 3E.

## Objetivo

Gestionar pipelines y datasources **sin tocar JSON a mano**: una UI de administracion
(`DataPipeline.Admin`) que calca el patron probado de `ConsultasGenericas.Admin`
(Blazor Server + Syncfusion, .NET 10), respaldada por endpoints de gestion en `DataPipeline.Api`.
La mecanica es identica a la del Admin existente; cambia el dominio.

**Criterio global (analisis, seccion 15 - Fase 3):** crear/editar/correr pipelines y ver corridas
sin tocar JSON.

## Estado de partida (Fase 2, ya construido)

- `DataPipeline.Api`: Basic Auth (`pull`), `GET /api/data/{nombre}`, `POST /api/run/{id}`,
  `GET /api/runs`, scheduler hosted. DI completa (repos EF, runner, factories, encryption).
- `DataPipeline.Persistence`: almacen A (EF Core/SQLite) con `MetadataDbContext`, `EfPipelineStore`,
  `EfHistorialCorridas`, migracion `InitMetadata`.
- `DataPipeline.Core`: `PipelineDefinition`, `StepDefinition` (`Configuracion` como
  `Dictionary<string,JsonElement>`), `PipelineRun`, contratos `IPipelineStore`/`IHistorialCorridas`,
  `IEncryptionService`, `ISourceFactory`/`ITransformFactory`. **Sin dependencias externas** (regla
  dura heredada de Fase 1/2).
- `DataPipeline.Connectors`: `SqlServerSource`, `SqliteDataStore`, `ColumnMapTransform` (NCalc),
  `SourceFactory`, `TransformFactory`, `EncryptionService`.
- Build pristine, 37 tests verdes. Workflow: commits directo a `main`, un commit por tarea.

## Molde a calcar

`D:\Sistemas\ConsultasGenericas` (accesible). Referencias clave:
- Patron Admin: `ConsultasGenericas.Admin/Components/Pages/*` + `Services/*AdminService.cs`.
- Auth Admin: `ConsultasGenericas.Admin/Services/AdminAuthService.cs` + `AdminSessionState.cs`
  (+ `admin-credentials.json` con `PasswordHash` BCrypt).
- Entidad origen: `ConsultasGenericas.Core/Entities/DataSource.cs`,
  `Enums/DataSourceType.cs`.
- El Admin **habla con la API por `HttpClient`, nunca toca la BD directo** (patron a calcar).

> El molde se **calca**, no se referencia: `DataPipeline` sigue siendo una solucion separada e
> independiente. No se agregan dependencias a `ConsultasGenericas.*`.

---

## 1) Descomposicion en rebanadas y orden

```
3A. API de gestion + auth admin       (backend)   --+
3B. DataSources: entidad + CRUD API    (backend)   --+-- desbloquean la UI
3C. Shell Admin (Blazor+Syncfusion)    (UI)        --+   (Syncfusion: licencia)
     + login + lista pipelines + /runs + correr
3D. Pantalla DataSources en el Admin   (UI)        --+
3E. Editor de pipeline + disenador      (UI)       --+   (la rebanada mas dificil)
     visual de steps (genera el JSON)
```

- **3A** es el cimiento: todas las pantallas hablan con la API de gestion.
- **3B** es independiente de 3A, pero ambas son prerequisito de la UI.
- **3C** trae el proyecto Blazor + Syncfusion y las pantallas read/trigger (las mas simples;
  validan el patron end-to-end).
- **3D** agrega la pantalla de datasources sobre 3B.
- **3E** es el grueso del riesgo (editor visual que arma el JSON) y va ultimo.

**Orden:** 3A -> 3B -> 3C -> 3D -> 3E.

---

## 2) Decisiones transversales

### Auth (dos capas, calcando el molde)

- **Usuario -> Admin:** login con una credencial de admin unica (hash BCrypt en config) +
  `AdminSessionState` (sesion del circuito Blazor Server). Sin multi-usuario por ahora (YAGNI;
  extensible despues).
- **Admin -> API:** el Admin habla con `DataPipeline.Api` por `HttpClient`. Los endpoints de
  gestion se protegen con una **credencial/rol `admin` via Basic Auth, separada del usuario
  `pull`** (que sigue siendo solo-lectura de datos en `/api/data`). Resultado: **dos credenciales
  Basic** — `pull` (data) y `admin` (gestion). El handler Basic existente se extiende para resolver
  ambos usuarios y exponer un rol; los controllers de gestion exigen rol `admin`.

### DataSource (entidad propia, almacen A)

Entidad nueva en el almacen A de DataPipeline (NO se referencia `ConsultasGenericas.Core`, para
respetar "Core sin dependencias externas"):

```
DataSource {
  Guid Id; string Nombre; TipoDataSource Tipo;
  string? ConnectionString;  // encriptado
  string? Username;          // encriptado
  string? Password;          // encriptado
  string? BaseUrl;           // futuro (RestApi)
  string? FilePath;          // futuro (Excel)
  bool Activo; DateTime CreatedAt; DateTime? UpdatedAt;
}
enum TipoDataSource { SqlServer }  // arranca con SqlServer; extensible
```

- Secretos encriptados con el `IEncryptionService` existente.
- El `StepDefinition` de tipo Origen pasa a poder referenciar un `DataSourceId`; el `SourceFactory`
  lo resuelve (lee el datasource, desencripta, arma el `SqlServerSource`). **Se conserva
  compatibilidad** con la config inline actual (`connectionString`/`sql` en `Configuracion`): si hay
  `DataSourceId`, se usa el datasource; si no, se cae a la config inline.

### Proyecto Admin

`DataPipeline.Admin` (Blazor Server + Syncfusion, .NET 10), patron calcado:
- Por entidad: `Components/Pages/{Entidad}/Index.razor` (grilla Syncfusion) + `Form.razor`.
- Un `{Entidad}AdminService.cs` por entidad que llama a la API por `HttpClient`.
- `MainLayout` + `NavMenu` + `Login`; `AdminAuthService` + `AdminSessionState`.
- Componentes Syncfusion: Grid, Inputs, DropDowns, Calendars, Notifications, Popups, Spinner.

### Dependencia / riesgo — Syncfusion

Requiere **clave de licencia** (el molde la usa). No bloquea 3A/3B (backend), **si bloquea 3C en
adelante**. Antes de arrancar 3C: confirmar licencia valida; si no hay, evaluar community license o
alternativa. **Gate explicito** al inicio de 3C.

---

## 3) Contrato y criterio por rebanada

### 3A — API de gestion + auth admin (backend)

- **Entrega:** rol `admin` en el Basic Auth handler; `GET /api/pipelines` (lista),
  `GET /api/pipelines/{id}`, `POST /api/pipelines` (crear/editar via `Upsert`),
  `DELETE /api/pipelines/{id}`. Reusa `IPipelineStore`.
- **Criterio:** CRUD completo de pipelines por HTTP autenticado como `admin`; `pull` no accede a
  gestion (403).

### 3B — DataSources: entidad + CRUD API (backend)

- **Entrega:** entidad `DataSource` + `IDataSourceStore` + repo EF + migracion; secretos
  encriptados; `GET/POST/DELETE /api/datasources` (rol admin) +
  `POST /api/datasources/{id}/probar` (test de conexion). `SourceFactory` resuelve `DataSourceId`.
- **Criterio:** crear/editar/borrar/probar un datasource por HTTP; un pipeline que referencia un
  `DataSourceId` corre.

### 3C — Shell Admin + pantallas read/trigger (UI, necesita Syncfusion)

- **Entrega:** proyecto `DataPipeline.Admin`, layout/nav/login, `AdminAuthService` +
  `AdminSessionState`, `HttpClient` a la API; `/pipelines` (grilla: estado, habilitado, proxima
  corrida), `/runs` (historial), boton "correr ahora".
- **Criterio:** loguearse, ver pipelines y corridas, disparar uno on-demand sin tocar JSON.

### 3D — Pantalla DataSources en el Admin (UI)

- **Entrega:** `/datasources` Index+Form, probar conexion desde la UI, `DataSourceAdminService`.
- **Criterio:** gestionar origenes desde la UI.

### 3E — Editor de pipeline + disenador visual de steps (UI, la mas dificil)

- **Entrega:** `/pipelines/editar/{id}` — form del pipeline (datasource, transforms
  rename/cast/computed/filter, tabla destino, schedule, `RunAfter`) con **gestion inline de steps**
  que genera/edita el JSON persistido.
- **Criterio:** crear y editar un pipeline completo visualmente, end-to-end, sin escribir JSON.

---

## 4) Fuera de alcance (Fase 3)

Diferido a fases posteriores (4/5):
- Multi-usuario / roles finos (Fase 3 = un solo admin).
- Watermark incremental, Polly/reintentos, dead-letter, idempotencia (Fase 4).
- `SqlServerDataStore`, 2do source (Fase 4).
- Ejecucion encadenada real (`RunAfter` se **edita** en 3E, pero su orquestacion encadenada es
  Fase 4).
- Entrega PUSH (Fase 5).

## 5) Trazabilidad con el analisis

- Analisis seccion 16 (UI DataPipeline.Admin) -> rebanadas 3C/3D/3E.
- Analisis seccion 15 Fase 3 (criterio) -> criterio global de este spec.
- Decision log #10 (UI Blazor + Syncfusion calcando el Admin) -> seccion 2 (Proyecto Admin).
- Decision log #2 (acoplamiento con Core) -> revisada: se opta por **entidad DataSource propia**
  en vez de referenciar `ConsultasGenericas.Core`, para preservar "Core sin deps externas"
  (regla dura de Fase 1/2).
