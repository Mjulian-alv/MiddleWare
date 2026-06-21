# DataPipeline — Fase 3C (Admin Shell + pantallas read/trigger) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. **Verificacion = build-only** (decision del usuario): cada tarea cierra con `dotnet build` limpio; la prueba funcional de la UI y de los servicios es **manual** en el navegador. No se agregan tests automaticos en esta rebanada.

**Goal:** Crear `DataPipeline.Admin` (Blazor Server + Syncfusion) calcando el patron de `ConsultasGenericas.Admin`: login local, layout/nav, y pantallas para listar pipelines, dispararlos on-demand y ver el historial de corridas — consumiendo `DataPipeline.Api` por `HttpClient` con la credencial `admin`.

**Architecture:** Blazor Web App interactivo en servidor (`AddRazorComponents().AddInteractiveServerComponents()`). Auth local del panel (`AdminAuthService` con password BCrypt en archivo + `AdminSessionState` por circuito); el `MainLayout` es el gate de sesion. Servicios `*AdminService` (HttpClient) hablan con la API; el `HttpClient` lleva `Authorization: Basic admin:****` desde config. Referencia solo a `DataPipeline.Core` (para tipos `PipelineDefinition`/`PipelineRun`); nunca toca la BD.

**Tech Stack:** .NET 10 (`Sdk.Web`), Blazor Server, Syncfusion.Blazor (Buttons/Grid/Inputs/Notifications/Popups/Spinner/DropDowns/Themes), BCrypt.Net-Next.

**Spec:** `docs/superpowers/specs/2026-06-21-datapipeline-fase3-umbrella-design.md` (rebanada 3C). Molde: `D:\Sistemas\ConsultasGenericas\ConsultasGenericas.Admin`.

## Global Constraints

- `net10.0`; Nullable/ImplicitUsings enable; `<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>`.
- Build pristine (0 warnings); verificar con `dotnet build --no-incremental`.
- Convencion de idioma (Opcion 2): props/dominio en castellano; servicios/metodos en ingles/.NET; locales/comentarios en espanol; sin tildes ni `ñ` en identificadores.
- Commits directo a `main`, un commit por tarea, con `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Licencia Syncfusion:** se registra desde config `SyncfusionLicenseKey` (el usuario coloca su clave en `appsettings` del Admin o variable de entorno; **NO se hardcodea en el repo**). Sin clave, Syncfusion muestra un banner de trial pero compila y corre.

## Decisiones de diseno (3C)

- **Admin -> API:** el `HttpClient` de cada `*AdminService` lleva `Authorization: Basic base64(admin:clave)` tomada de config `ApiAuth:Username`/`ApiAuth:Password` (default `admin`/`admin123` para dev; el usuario la cambia). La URL base de la API en config `ApiBaseUrl`.
- **Verificacion build-only:** sin tests nuevos; la logica de servicios y la UI se prueban manualmente.
- **Alcance 3C:** login, layout/nav, `/` (home), `/pipelines` (grilla + correr ahora), `/runs` (historial). La pantalla `/datasources` es 3D; el editor `/pipelines/editar/{id}` es 3E (los links de nav se agregan en esas rebanadas).

---

## File Structure

```
DataPipeline.Admin/                          (NEW PROJECT, Sdk.Web)
  DataPipeline.Admin.csproj
  Program.cs
  appsettings.json
  Services/AdminAuthService.cs               (password local BCrypt en archivo)
  Services/AdminSessionState.cs              (sesion por circuito)
  Services/PipelinesAdminService.cs          (HttpClient -> /api/pipelines, /api/run)
  Services/RunsAdminService.cs               (HttpClient -> /api/runs)
  Services/RunResultDto.cs                   (shape del resultado de correr)
  Components/App.razor                        (host HTML + Syncfusion theme/js)
  Components/Routes.razor
  Components/_Imports.razor
  Components/Layout/MainLayout.razor          (gate de sesion + sidebar)
  Components/Layout/EmptyLayout.razor         (login)
  Components/Layout/NavMenu.razor
  Components/Pages/Login.razor
  Components/Pages/Home.razor
  Components/Pages/Pipelines/Index.razor      (grilla + correr ahora)
  Components/Pages/Runs/Index.razor           (historial)
  Components/Pages/Error.razor, NotFound.razor
DataPipeline.slnx                             (MOD: + DataPipeline.Admin)
```

---

### Task 1: Crear el proyecto `DataPipeline.Admin` + paquetes + Program.cs + appsettings

**Files:** Create project, `DataPipeline.Admin.csproj`, `Program.cs`, `appsettings.json`; Modify `DataPipeline.slnx`.

- [ ] **Step 1: Crear el proyecto Blazor Web App + referencias + paquetes**

```bash
dotnet new blazor -o DataPipeline.Admin --framework net10.0 --interactivity Server
dotnet add DataPipeline.Admin reference DataPipeline.Core
dotnet add DataPipeline.Admin package BCrypt.Net-Next
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Buttons
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Grid
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Inputs
dotnet add DataPipeline.Admin package Syncfusion.Blazor.DropDowns
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Notifications
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Popups
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Spinner
dotnet add DataPipeline.Admin package Syncfusion.Blazor.Themes
dotnet sln DataPipeline.slnx add DataPipeline.Admin
```
Asegurar en el csproj `<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>` en el PropertyGroup.

- [ ] **Step 2: Reemplazar `Program.cs`** (Syncfusion + auth + HttpClients con Basic admin)

(contenido completo en la ejecucion; calca el molde: RegisterLicense desde `SyncfusionLicenseKey`, `AddSyncfusionBlazor()`, `AddRazorComponents().AddInteractiveServerComponents()`, `AddSingleton<AdminAuthService>`, `AddScoped<AdminSessionState>`, un `AddHttpClient<T>` por servicio con `BaseAddress=ApiBaseUrl` y header `Authorization: Basic base64(ApiAuth:Username:ApiAuth:Password)`, opcion `ApiAcceptAnyServerCertificate`).

- [ ] **Step 3: `appsettings.json`** con `SyncfusionLicenseKey` (vacio), `ApiBaseUrl`, `ApiAuth:Username`/`Password`, `Logging`, `AllowedHosts`.

- [ ] **Step 4: Build** `dotnet build DataPipeline.Admin --nologo` -> compila (las clases de Services/Components se agregan en tareas siguientes; en este punto, dejar Program.cs referenciando solo lo que ya existe del template o crear stubs minimos). 

> Nota de ejecucion: para que el build cierre en cada paso, crear las clases de Services (Tasks 2-3) ANTES de referenciarlas en Program.cs, o construir Program.cs incrementalmente. El orden real de ejecucion crea Services y Components y deja Program.cs completo al final de Task 4.

- [ ] **Step 5: Commit** `feat(admin): proyecto DataPipeline.Admin (Blazor Server + Syncfusion) + wiring base`

---

### Task 2: Auth del panel — `AdminAuthService` + `AdminSessionState` + `EmptyLayout`

**Files:** Create `Services/AdminAuthService.cs`, `Services/AdminSessionState.cs`, `Components/Layout/EmptyLayout.razor`.

- [ ] **Step 1-3:** Calcar del molde `AdminAuthService` (usuario unico `dpAdmin`, password por defecto configurable, hash BCrypt en `admin-credentials.json` del ContentRoot) y `AdminSessionState` (IsAuthenticated/Username/OnChange, Autenticar/CerrarSesion). `EmptyLayout` = layout minimo sin sidebar para el login.
- [ ] **Step 4: Build** limpio.
- [ ] **Step 5: Commit** `feat(admin): auth local del panel (AdminAuthService + AdminSessionState)`

---

### Task 3: Servicios de API — `PipelinesAdminService` + `RunsAdminService`

**Files:** Create `Services/PipelinesAdminService.cs`, `Services/RunsAdminService.cs`, `Services/RunResultDto.cs`.

**Interfaces:**
- `PipelinesAdminService`: `Task<List<PipelineDefinition>> ObtenerTodosAsync()` (GET api/pipelines), `Task<PipelineDefinition?> ObtenerPorIdAsync(Guid)` (GET api/pipelines/{id}), `Task<bool> GuardarAsync(PipelineDefinition)` (POST api/pipelines), `Task<bool> EliminarAsync(Guid)` (DELETE), `Task<RunResultDto?> CorrerAsync(Guid)` (POST api/run/{id}).
- `RunsAdminService`: `Task<List<PipelineRun>> ObtenerRecientesAsync(int max)` (GET api/runs?max=).
- `record RunResultDto(Guid RunId, bool Success, long RowsRead, long RowsWritten, string? Error)`.

- [ ] **Steps:** Implementar (HttpClient + System.Text.Json case-insensitive, calcando el patron del molde). Build limpio. Commit `feat(admin): servicios HttpClient PipelinesAdminService + RunsAdminService`.

---

### Task 4: Shell — App/Routes/_Imports/MainLayout/NavMenu/Login/Home

**Files:** Replace/create `Components/App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, `Pages/Login.razor`, `Pages/Home.razor`. Completar `Program.cs` (Task 1 Step 2) referenciando ya todos los servicios.

- [ ] **Steps:** Calcar del molde: `App.razor` con `_content/Syncfusion.Blazor.Themes/bootstrap5.css` + `blazor.web.js` + `syncfusion-blazor.min.js`; `Routes.razor` interactive server; `_Imports.razor` con usings Syncfusion + `DataPipeline.Admin.*` + `DataPipeline.Core.Pipelines`; `MainLayout` gate de sesion (redirige a `/login` si no autenticado) + boton Salir; `NavMenu` con links Inicio / Pipelines / Corridas; `Login.razor` (calca el del molde, usa `AdminAuthService`/`AdminSessionState`); `Home.razor` simple. Build limpio. Commit `feat(admin): shell Blazor (layout, nav, login, home)`.

---

### Task 5: Pantallas — `/pipelines` (grilla + correr ahora) y `/runs` (historial)

**Files:** Create `Components/Pages/Pipelines/Index.razor`, `Components/Pages/Runs/Index.razor`.

- [ ] **Steps:**
  - `Pipelines/Index.razor` (`@page "/pipelines"`): `SfGrid` sobre `List<PipelineDefinition>` (columnas: Nombre, Habilitado, Programacion, ProximaCorrida, UltimaCorrida, TablaDestino); boton "Correr ahora" por fila que llama `PipelinesAdminService.CorrerAsync(id)` y muestra el `RunResultDto` con un `SfToast`/mensaje. Carga via `ObtenerTodosAsync()` en `OnInitializedAsync`.
  - `Runs/Index.razor` (`@page "/runs"`): `SfGrid` sobre `List<PipelineRun>` (columnas: Inicio, Fin, Estado, FilasLeidas, FilasEscritas, Mensaje), via `RunsAdminService.ObtenerRecientesAsync(50)`.
  - Build `dotnet build --no-incremental` limpio. Commit `feat(admin): pantallas /pipelines (correr ahora) y /runs (historial)`.

---

## Self-Review

**1. Spec coverage (3C):** proyecto Admin + login + layout/nav (Tasks 1,2,4) ✅; `/pipelines` grilla + correr ahora (Task 5) ✅; `/runs` historial (Task 5) ✅; HttpClient con auth admin (Task 1/3) ✅.
**2. Placeholder scan:** sin TODO. `SyncfusionLicenseKey` vacio a proposito (lo coloca el usuario). `ApiAuth` default dev.
**3. Type consistency:** servicios deserializan `PipelineDefinition`/`PipelineRun` (Core) y `RunResultDto`; las grillas bindean esas props. `AdminAuthService.UsuarioAdmin`/`PasswordPorDefecto` usados por `Login.razor`.
**4. Notas de alcance:** verificacion build-only (sin tests). UI/servicios = prueba manual. `/datasources` (3D) y editor (3E) fuera de 3C; sus links de nav se agregan al implementarlas.
