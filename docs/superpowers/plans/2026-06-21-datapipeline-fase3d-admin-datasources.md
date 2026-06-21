# DataPipeline — Fase 3D (Pantalla DataSources en el Admin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. La capa API se testea (TDD); la UI del Admin es **build-only** (decision del usuario en 3C, se mantiene).

**Goal:** Gestionar DataSources desde el Admin (grilla + form alta/edicion + probar conexion), calcando el patron `DataSources/Index+Form` del molde y aplicando buenas practicas de forms (ui-ux-pro-max §8). Ademas, corregir la semantica de update del API para que **un secreto vacio conserve el anterior** (evita perdida de datos al editar).

**Architecture:** `DataSourcesAdminService` (HttpClient) consume `/api/datasources`. `DataSources/Index.razor` (grilla Syncfusion + acciones: nuevo, editar, probar, eliminar-con-confirmacion) + un form modal (`SfDialog`) con campos Nombre/Tipo/ConnectionString/Username/Password/Activo. Secretos nunca pre-cargados; en edicion, vacio = conservar. La API `DataSourcesController.Upsert` se ajusta para esa semantica.

**Tech Stack:** Blazor Server + Syncfusion (Grids, Buttons, Inputs, DropDowns, Popups), ASP.NET Core API (existente).

**Spec:** umbrella 3D. Decisiones heredadas: GET nunca devuelve secretos (3B); auth admin (3A).

## Global Constraints
- net10.0; build pristine (`dotnet build --no-incremental`); convencion de idioma (sin tildes/ñ en identificadores).
- Commits directo a `main`, uno por tarea, con `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

### Task 1 (API): update con secreto vacio conserva el anterior

**Files:** Modify `DataPipeline.Api/Controllers/DataSourcesController.cs`; Test `tests/DataPipeline.Api.Tests/DataSourcesApiTests.cs`.

- [ ] **Step 1:** Test (agregar a `DataSourcesApiTests`): crear ds con connectionString; hacer POST de update con mismo Id, Nombre cambiado y ConnectionString=null; GET vista -> `TieneConnectionString` sigue true (no se borro).
- [ ] **Step 2:** RED (`dotnet test --filter DataSourcesApiTests`).
- [ ] **Step 3:** En `Upsert`, para cada secreto: `string.IsNullOrEmpty(req.X) ? existente?.X : Enc(req.X)`.
- [ ] **Step 4:** GREEN + build pristine.
- [ ] **Step 5:** Commit `fix(api): update de DataSource con secreto vacio conserva el valor anterior`.

---

### Task 2 (Admin): `DataSourcesAdminService` + registro + link de nav

**Files:** Create `DataPipeline.Admin/Services/DataSourcesAdminService.cs`; Modify `Program.cs`, `Components/Layout/NavMenu.razor`.

- [ ] Implementar el servicio (GET list/by-id, POST upsert, DELETE, POST probar) con DTOs `DataSourceVista`/`DataSourceUpsert`/`ProbarResp`. Registrar `ApiClient<DataSourcesAdminService>()`. Agregar link "Origenes" a NavMenu. Build limpio. Commit `feat(admin): DataSourcesAdminService + nav`.

---

### Task 3 (Admin): pantalla `/datasources` (grilla + form modal + probar)

**Files:** Create `DataPipeline.Admin/Components/Pages/DataSources/Index.razor`.

- [ ] Grilla Syncfusion (Nombre, Tipo, Activo, CreatedAt, UpdatedAt) + boton "Nuevo origen" (unico CTA primario). Acciones por fila: Editar (abre modal), Probar (POST probar, feedback exito/error), Eliminar (SfDialog de confirmacion, enfasis danger). Form en `SfDialog`: Nombre (requerido, error debajo), Tipo (dropdown SqlServer), ConnectionString/Username/Password (secretos, toggle show/hide, helper "deja vacio para conservar"), Activo (switch). Guardar con loading + feedback; Cancelar. Build pristine. Commit `feat(admin): pantalla /datasources (grilla, form modal, probar conexion)`.

## Self-Review
- UX (ui-ux-pro-max §8): input-labels, error-placement, submit-feedback, required-indicators, password-toggle, disabled-states, error-clarity, destructive-emphasis + confirm, success-feedback, secreto "vacio = conservar".
- Cobertura: API keep-on-empty (Task 1, con test); UI gestion + probar (Tasks 2-3, build-only).
- Fuera de 3D: editor de pipelines (3E).
