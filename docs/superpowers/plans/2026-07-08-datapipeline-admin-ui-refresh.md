# DataPipeline.Admin — Renovacion de UI (reskin) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renovar la UI de `DataPipeline.Admin` reskinneando sobre lo existente con el design system, el shell y los patrones del Admin actual de ConsultasGenericas, sin tocar logica.

**Architecture:** Se porta el design system de CG (`utilities.css` + `app.css` + assets de marca), se reemplaza el shell (`MainLayout`/`NavMenu`) y se repasa cada pantalla cambiando clases Bootstrap por utilities y adoptando los patrones (`page-header`, `e-btn` con gradiente, `e-badge`, `SfMessage`, `panel`). El `@code` de cada pantalla y todos los servicios/contratos quedan intactos.

**Tech Stack:** Blazor Server (.NET 10), Syncfusion Blazor (tema `bootstrap5`), CSS propio (utilities.css/app.css).

## Global Constraints

- **Reskin puro:** no se modifica ningun bloque `@code`, servicio, DTO, auth ni contrato HTTP. Solo markup (`.razor` fuera de `@code`), CSS y `wwwroot`.
- **Sin unit tests** en este plan: los escribe el usuario. Verificacion = build limpio + revision visual manual.
- **Gate de build:** cada tarea cierra con `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental` y **0 warnings**.
- **Commits directos a `main`, rutas explicitas** en `git add` (nunca `git add -A`).
- **Idioma:** identificadores en ingles; copy visible en castellano (con tildes ok en texto, sin tildes/ñ en identificadores).
- **Fuente de los ports:** `D:\Sistemas\ConsultasGenericas\ConsultasGenericas.Admin` (referido abajo como `CG`). El Admin destino es `DataPipeline.Admin` (referido como `DP`).
- **`_Imports.razor` de DP ya tiene** `Syncfusion.Blazor.Notifications` (SfMessage), `Spinner`, `Popups`, `Buttons`, `Grids`, `Inputs`, `DropDowns` — no hace falta agregar usings.

---

### Task 1: Design system + assets de marca + App.razor

**Files:**
- Create: `DataPipeline.Admin/wwwroot/utilities.css` (copia verbatim de CG)
- Create: `DataPipeline.Admin/wwwroot/brand/logo.png`, `.../brand/icon.png`, `.../brand/icon-512.png`, `DataPipeline.Admin/wwwroot/favicon.ico` (copias de CG)
- Modify: `DataPipeline.Admin/wwwroot/app.css` (reemplazo total por la version de CG)
- Modify: `DataPipeline.Admin/Components/App.razor:10,2,14`

**Interfaces:**
- Produces: las clases del design system (`page-header`, `panel`, `icon-tile`, `e-btn` gradiente, `e-badge` pill, `texto-*`, utilidades flex/spacing, animaciones) y los assets `brand/*` que consumen las tareas 2-9.

- [ ] **Step 1: Copiar utilities.css, app.css y assets de marca desde CG**

```bash
cd "D:/Sistemas/MiddleWare"
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/utilities.css" DataPipeline.Admin/wwwroot/utilities.css
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/app.css"       DataPipeline.Admin/wwwroot/app.css
mkdir -p DataPipeline.Admin/wwwroot/brand
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/brand/logo.png"      DataPipeline.Admin/wwwroot/brand/logo.png
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/brand/icon.png"      DataPipeline.Admin/wwwroot/brand/icon.png
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/brand/icon-512.png"  DataPipeline.Admin/wwwroot/brand/icon-512.png
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/wwwroot/favicon.ico"         DataPipeline.Admin/wwwroot/favicon.ico
```

- [ ] **Step 2: Editar `App.razor` — cargar utilities.css, quitar Bootstrap, lang=es, favicon.ico**

Reemplazar en `DataPipeline.Admin/Components/App.razor`:

Linea 2: `<html lang="en">` → `<html lang="es">`

Linea 10: `    <link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />`
→ `    <link rel="stylesheet" href="@Assets["utilities.css"]" />`

Linea 14: `    <link rel="icon" type="image/png" href="favicon.png" />`
→ `    <link rel="icon" type="image/x-icon" href="favicon.ico" />`

(El resto del `<head>` queda igual: tema `bootstrap5` de Syncfusion, `app.css`, `DataPipeline.Admin.styles.css`, `ImportMap`, `HeadOutlet`.)

- [ ] **Step 3: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

> Nota: tras quitar Bootstrap, las pantallas que aun usan clases Bootstrap (`alert`, `container-fluid`, etc.) pueden verse raras hasta su tarea de reskin. El build no lo detecta (son clases CSS). Es esperado y se corrige en las tareas 2-9.

- [ ] **Step 4: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/wwwroot/utilities.css DataPipeline.Admin/wwwroot/app.css DataPipeline.Admin/wwwroot/brand DataPipeline.Admin/wwwroot/favicon.ico DataPipeline.Admin/Components/App.razor
git commit -m "feat(admin): design system (utilities.css + app.css) y assets de marca; quita Bootstrap del head"
```

---

### Task 2: Shell — MainLayout + NavMenu

**Files:**
- Modify: `DataPipeline.Admin/Components/Layout/MainLayout.razor` (markup; `@code` intacto)
- Modify: `DataPipeline.Admin/Components/Layout/MainLayout.razor.css` (reemplazo total por la version de CG)
- Modify: `DataPipeline.Admin/Components/Layout/NavMenu.razor` (markup completo)
- Modify: `DataPipeline.Admin/Components/Layout/NavMenu.razor.css` (reemplazo total por la version de CG)

**Interfaces:**
- Consumes: clases del design system (Task 1), assets `brand/icon.png`.
- Produces: shell renovado (sidebar gradiente, top-row con chip de usuario, nav con marca + secciones + e-icons).

- [ ] **Step 1: Copiar los CSS de shell desde CG**

```bash
cd "D:/Sistemas/MiddleWare"
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/Components/Layout/MainLayout.razor.css" DataPipeline.Admin/Components/Layout/MainLayout.razor.css
cp "D:/Sistemas/ConsultasGenericas/ConsultasGenericas.Admin/Components/Layout/NavMenu.razor.css"     DataPipeline.Admin/Components/Layout/NavMenu.razor.css
```

- [ ] **Step 2: Reescribir el markup de `MainLayout.razor` (top-row con chip de usuario)**

Reemplazar el bloque `@if (Session.IsAuthenticated) { ... }` (lineas 6-27 del archivo actual) por:

```razor
@if (Session.IsAuthenticated)
{
    <div class="page">
        <div class="sidebar">
            <NavMenu />
        </div>

        <main>
            <div class="top-row px-4 d-flex justify-space-between align-center">
                <span></span>
                <span class="d-flex align-center gap-3">
                    <span class="d-inline-flex align-center gap-2 texto-body2" style="color:#475569;">
                        <span style="width:28px;height:28px;border-radius:50%;background:rgba(99,102,241,.12);color:#6366f1;display:inline-flex;align-items:center;justify-content:center;">
                            <span class="e-icons e-user" style="font-size:.8rem;"></span>
                        </span>
                        @Session.Username
                    </span>
                    <SfButton CssClass="e-flat e-small" OnClick="CerrarSesion">Salir</SfButton>
                </span>
            </div>

            <article class="content px-4">
                @Body
            </article>
        </main>
    </div>
}
```

(El `<div id="blazor-error-ui">` y todo el `@code` quedan sin cambios.)

- [ ] **Step 3: Reescribir `NavMenu.razor` completo (marca + secciones + e-icons)**

Reemplazar TODO el contenido de `DataPipeline.Admin/Components/Layout/NavMenu.razor` por:

```razor
<div class="top-row navbar">
    <div class="brand">
        <img class="brand-mark" src="brand/icon.png" alt="Alvear Sistemas" />
        <span class="brand-text">DataPipeline<small>Panel de Administracion</small></span>
    </div>
</div>

<input type="checkbox" title="Menu de navegacion" class="navbar-toggler" />

<div class="nav-scrollable" onclick="document.querySelector('.navbar-toggler').click()">
    <nav class="nav flex-column">

        <span class="nav-section">General</span>

        <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
            <span class="e-icons e-home" aria-hidden="true"></span> Inicio
        </NavLink>

        <span class="nav-section">Datos</span>

        <NavLink class="nav-link" href="datasources">
            <span class="e-icons e-folder" aria-hidden="true"></span> Origenes
        </NavLink>

        <NavLink class="nav-link" href="pipelines">
            <span class="e-icons e-settings" aria-hidden="true"></span> Pipelines
        </NavLink>

        <NavLink class="nav-link" href="runs">
            <span class="e-icons e-history" aria-hidden="true"></span> Corridas
        </NavLink>
    </nav>
</div>
```

> Icono de Corridas: si `e-history` no renderiza con el tema `bootstrap5`, cambiar por `e-refresh` (se detecta solo en la revision visual, no en el build).

- [ ] **Step 4: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Layout/MainLayout.razor DataPipeline.Admin/Components/Layout/MainLayout.razor.css DataPipeline.Admin/Components/Layout/NavMenu.razor DataPipeline.Admin/Components/Layout/NavMenu.razor.css
git commit -m "feat(admin): shell renovado (sidebar gradiente, top-row con usuario, nav con marca y e-icons)"
```

---

### Task 3: Home (landing de cards)

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Home.razor` (reemplazo total)

**Interfaces:**
- Consumes: `page-header`, `fila-grid`, `panel-hover`, `icon-tile`, `e-arrow-right`, `texto-*` (Task 1).

- [ ] **Step 1: Reescribir `Home.razor` completo**

Reemplazar TODO el contenido de `DataPipeline.Admin/Components/Pages/Home.razor` por:

```razor
@page "/"

<PageTitle>Inicio — DataPipeline Admin</PageTitle>

<div class="page-header">
    <div>
        <h1 class="page-title">Panel de Administracion</h1>
        <p class="page-subtitle">Gestiona origenes de datos, pipelines y monitorea las corridas.</p>
    </div>
</div>

<div class="fila-grid">
    <div class="col-sm-4">
        <a href="/pipelines" style="text-decoration:none;">
            <div class="panel panel-hover pa-4 d-flex flex-column gap-3" style="height:100%;">
                <span class="icon-tile"><span class="e-icons e-settings"></span></span>
                <div class="flex-grow-1">
                    <h6 class="mb-1">Pipelines</h6>
                    <p class="texto-body2 texto-secundario" style="margin:0;">
                        Definicion de pipelines Source-Transform-Store, edicion y corrida on-demand.
                    </p>
                </div>
                <span class="texto-body2" style="color:var(--brand);font-weight:600;">
                    Ir a Pipelines <span class="e-icons e-arrow-right" style="font-size:.7rem;"></span>
                </span>
            </div>
        </a>
    </div>
    <div class="col-sm-4">
        <a href="/datasources" style="text-decoration:none;">
            <div class="panel panel-hover pa-4 d-flex flex-column gap-3" style="height:100%;">
                <span class="icon-tile" style="background:rgba(6,182,212,.12);color:#06b6d4;"><span class="e-icons e-folder"></span></span>
                <div class="flex-grow-1">
                    <h6 class="mb-1">Origenes de datos</h6>
                    <p class="texto-body2 texto-secundario" style="margin:0;">
                        Conexiones a las bases de datos que alimentan los pipelines.
                    </p>
                </div>
                <span class="texto-body2" style="color:var(--brand);font-weight:600;">
                    Ir a Origenes <span class="e-icons e-arrow-right" style="font-size:.7rem;"></span>
                </span>
            </div>
        </a>
    </div>
    <div class="col-sm-4">
        <a href="/runs" style="text-decoration:none;">
            <div class="panel panel-hover pa-4 d-flex flex-column gap-3" style="height:100%;">
                <span class="icon-tile" style="background:rgba(16,185,129,.12);color:#10b981;"><span class="e-icons e-history"></span></span>
                <div class="flex-grow-1">
                    <h6 class="mb-1">Corridas</h6>
                    <p class="texto-body2 texto-secundario" style="margin:0;">
                        Historial de ejecuciones con filas leidas, escritas y estado.
                    </p>
                </div>
                <span class="texto-body2" style="color:var(--brand);font-weight:600;">
                    Ir a Corridas <span class="e-icons e-arrow-right" style="font-size:.7rem;"></span>
                </span>
            </div>
        </a>
    </div>
</div>
```

- [ ] **Step 2: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 3: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Home.razor
git commit -m "feat(admin): Home como landing de cards (Pipelines/Origenes/Corridas)"
```

---

### Task 4: Login

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Login.razor` (solo markup, lineas 9-44; `@code` intacto)

**Interfaces:**
- Consumes: `panel`, `pa-8`, `cg-pop`, `texto-caption`, `texto-secundario`, `SfMessage`, `e-primary w-100`, asset `brand/logo.png` (Task 1).

- [ ] **Step 1: Reemplazar el markup del Login**

Reemplazar el bloque `<div style="min-height:100vh;...">...</div>` (lineas 9-44 actuales, hasta antes de `@code`) por:

```razor
<div style="min-height:100vh;display:flex;align-items:center;justify-content:center;padding:1rem;background:radial-gradient(ellipse at top, #eef0ff 0%, #f6f7fb 55%);">
    <div class="panel pa-8 cg-pop" style="width:100%;max-width:440px;box-shadow:0 12px 48px rgba(30,27,75,.12);">
        <div style="display:flex;flex-direction:column;align-items:center;gap:.6rem;margin-bottom:1.5rem;">
            <img src="brand/logo.png" alt="Alvear Sistemas" style="width:220px;max-width:75%;height:auto;" />
            <h5 style="text-align:center;margin-top:.4rem;">Panel de Administracion</h5>
            <p class="texto-caption texto-secundario" style="margin:0;">DataPipeline</p>
        </div>

        @if (_modoConfiguracion)
        {
            <p class="texto-body2 mb-6" style="text-align:center;color:#888;">
                Primera vez: defini la contrasena del administrador. Podes dejar la sugerida o cambiarla.
            </p>
        }

        @if (_error is not null)
        {
            <SfMessage Severity="MessageSeverity.Error" CssClass="mb-4">@_error</SfMessage>
        }

        <div class="mb-4">
            <SfTextBox Placeholder="Usuario" FloatLabelType="FloatLabelType.Always"
                       Value="@AdminAuthService.UsuarioAdmin" Enabled="false" />
        </div>

        <div class="mb-6" @onkeydown="OnKeyDown">
            <SfTextBox Placeholder="@(_modoConfiguracion ? "Nueva contrasena" : "Contrasena")"
                       FloatLabelType="FloatLabelType.Always"
                       Type="InputType.Password"
                       @bind-Value="_password" Enabled="@(!_cargando)" />
        </div>

        <SfButton CssClass="e-primary w-100" Disabled="_cargando" OnClick="Confirmar">
            @(_modoConfiguracion ? "Guardar y entrar" : "Ingresar")
        </SfButton>
    </div>
</div>
```

- [ ] **Step 2: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 3: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Login.razor
git commit -m "feat(admin): Login con panel, logo y SfMessage (patron CG)"
```

---

### Task 5: DataSources/Index

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/DataSources/Index.razor` (markup; `@code` intacto)

**Interfaces:**
- Consumes: `page-header`, `e-btn e-primary`, `e-badge`, `SfMessage`, `texto-secundario` (Task 1).

- [ ] **Step 1: Reemplazar header + feedback (lineas 6-14 actuales)**

Reemplazar:

```razor
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="mb-0">Origenes de datos</h1>
    <SfButton CssClass="e-primary" OnClick="NuevoOrigen">Nuevo origen</SfButton>
</div>

@if (_feedback is not null)
{
    <div class="alert @(_feedbackOk ? "alert-success" : "alert-danger")" role="alert" aria-live="polite">@_feedback</div>
}
```

Por:

```razor
<div class="page-header">
    <div>
        <h1 class="page-title">Origenes de datos</h1>
        <p class="page-subtitle">Conexiones a las bases de datos que alimentan los pipelines.</p>
    </div>
    <SfButton CssClass="e-primary" OnClick="NuevoOrigen">
        <span class="e-icons e-plus" style="margin-right:.4rem;font-size:.75rem;"></span>Nuevo origen
    </SfButton>
</div>

@if (_feedback is not null)
{
    <SfMessage Severity="@(_feedbackOk ? MessageSeverity.Success : MessageSeverity.Error)" CssClass="mb-3">@_feedback</SfMessage>
}
```

- [ ] **Step 2: Columna Activo como badge**

Reemplazar `<GridColumn Field="@nameof(DataSourceVista.Activo)" HeaderText="Activo" Width="90"></GridColumn>` por:

```razor
<GridColumn HeaderText="Activo" Width="100" TextAlign="TextAlign.Center">
    <Template Context="ctx">
        @{ var d = (DataSourceVista)ctx; }
        <span class="e-badge @(d.Activo ? "e-badge-success" : "e-badge-danger")">@(d.Activo ? "Si" : "No")</span>
    </Template>
</GridColumn>
```

- [ ] **Step 3: Reemplazar utilidades Bootstrap residuales del form modal**

En el `SfDialog` de alta/edicion:
- `<small class="text-danger">@_errorNombre</small>` → `<small style="color:var(--error);">@_errorNombre</small>`
- `<small class="text-muted d-block mb-2">Deja los secretos vacios para conservar los actuales.</small>` → `<small class="texto-secundario d-block mb-2">Deja los secretos vacios para conservar los actuales.</small>`
- `<div class="alert alert-danger py-2" role="alert">@_errorForm</div>` → `<SfMessage Severity="MessageSeverity.Error" CssClass="mb-2">@_errorForm</SfMessage>`

(Las clases `mb-2`/`mb-3`/`d-flex`/`justify-content-end` que queden dentro del dialog se dejan: `mb-*` y `d-flex` existen en utilities.css; `justify-content-end` no existe pero es cosmetico menor — si molesta, cambiar por `style="display:flex;justify-content:flex-end;"`.)

- [ ] **Step 4: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/DataSources/Index.razor
git commit -m "feat(admin): reskin DataSources (page-header, badge Activo, SfMessage)"
```

---

### Task 6: Pipelines/Index

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Index.razor` (markup; `@code` intacto)

**Interfaces:**
- Consumes: `page-header`, `e-btn e-primary`, `e-badge`, `SfMessage` (Task 1).

- [ ] **Step 1: Reemplazar header + feedback (lineas 7-15 actuales)**

Reemplazar:

```razor
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="mb-0">Pipelines</h1>
    <SfButton CssClass="e-primary" OnClick="@(() => Nav.NavigateTo("/pipelines/nuevo"))">Nuevo pipeline</SfButton>
</div>

@if (_mensaje is not null)
{
    <div class="alert @(_mensajeOk ? "alert-success" : "alert-danger")" role="alert">@_mensaje</div>
}
```

Por:

```razor
<div class="page-header">
    <div>
        <h1 class="page-title">Pipelines</h1>
        <p class="page-subtitle">Definicion de pipelines, edicion y corrida on-demand.</p>
    </div>
    <SfButton CssClass="e-primary" OnClick="@(() => Nav.NavigateTo("/pipelines/nuevo"))">
        <span class="e-icons e-plus" style="margin-right:.4rem;font-size:.75rem;"></span>Nuevo pipeline
    </SfButton>
</div>

@if (_mensaje is not null)
{
    <SfMessage Severity="@(_mensajeOk ? MessageSeverity.Success : MessageSeverity.Error)" CssClass="mb-3">@_mensaje</SfMessage>
}
```

- [ ] **Step 2: Columna Habilitado como badge**

Reemplazar `<GridColumn Field="@nameof(PipelineDefinition.Habilitado)" HeaderText="Habilitado" Width="110"></GridColumn>` por:

```razor
<GridColumn HeaderText="Habilitado" Width="120" TextAlign="TextAlign.Center">
    <Template Context="ctx">
        @{ var p = (PipelineDefinition)ctx; }
        <span class="e-badge @(p.Habilitado ? "e-badge-success" : "e-badge-danger")">@(p.Habilitado ? "Si" : "No")</span>
    </Template>
</GridColumn>
```

- [ ] **Step 3: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 4: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Pipelines/Index.razor
git commit -m "feat(admin): reskin Pipelines/Index (page-header, badge Habilitado, SfMessage)"
```

---

### Task 7: Runs/Index

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Runs/Index.razor` (markup; `@code` intacto)

**Interfaces:**
- Consumes: `page-header`, `e-badge` (Task 1).

- [ ] **Step 1: Reemplazar el `<h1>` por page-header**

Reemplazar `<h1>Corridas</h1>` por:

```razor
<div class="page-header">
    <div>
        <h1 class="page-title">Corridas</h1>
        <p class="page-subtitle">Historial de ejecuciones de los pipelines.</p>
    </div>
</div>
```

- [ ] **Step 2: Columna Estado como badge**

`PipelineRun.Estado` es el enum `EstadoCorrida { EnCurso, Ok, Fallida }` (en `DataPipeline.Core.Pipelines`, ya importado en `_Imports.razor`). Reemplazar `<GridColumn Field="@nameof(PipelineRun.Estado)" HeaderText="Estado" Width="110"></GridColumn>` por (sin tocar `@code` — el mapeo va inline en el Template):

```razor
<GridColumn HeaderText="Estado" Width="120" TextAlign="TextAlign.Center">
    <Template Context="ctx">
        @{
            var r = (PipelineRun)ctx;
            var badge = r.Estado switch
            {
                EstadoCorrida.Ok => "e-badge-success",
                EstadoCorrida.Fallida => "e-badge-danger",
                _ => "e-badge-warning"
            };
        }
        <span class="e-badge @badge">@r.Estado</span>
    </Template>
</GridColumn>
```

- [ ] **Step 3: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 4: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Runs/Index.razor
git commit -m "feat(admin): reskin Runs/Index (page-header, badge de estado)"
```

---

### Task 8: Pipelines/Editor + KeyValueEditor

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor` (markup, lineas 9-166; `@code` intacto)
- Modify: `DataPipeline.Admin/Components/Shared/KeyValueEditor.razor` (markup; `@code` intacto)

**Interfaces:**
- Consumes: `page-header`, `panel`, `panel-header`, `pa-4`, `e-btn` variantes, `texto-secundario`, `SfMessage` (Task 1). Respeta los controles del schema-discovery (dropdown watermark, multiselect clave, boton "Traer columnas").

- [ ] **Step 1: Reemplazar el markup del Editor (todo el bloque entre `<PageTitle>` y `@code`)**

Reemplazar las lineas 9-166 (desde `<div class="d-flex justify-content-between...` hasta el `}` que cierra el `else`, inclusive) por:

```razor
<div class="page-header">
    <div>
        <h1 class="page-title">@(Id is null ? "Nuevo pipeline" : "Editar pipeline")</h1>
    </div>
    <SfButton CssClass="e-flat" OnClick="@(() => Nav.NavigateTo("/pipelines"))">Volver a la lista</SfButton>
</div>

@if (_error is not null)
{
    <SfMessage Severity="MessageSeverity.Error" CssClass="mb-3">@_error</SfMessage>
}

@if (_cargando)
{
    <div style="position:relative;min-height:80px;"><SfSpinner Visible="true" /></div>
}
else
{
    <div class="panel mb-4">
        <div class="panel-header">Generales</div>
        <div class="pa-4">
            <div class="mb-3">
                <SfTextBox Placeholder="Nombre *" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.Nombre" />
            </div>
            <div class="mb-3">
                <SfTextBox Placeholder="Tabla destino *" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.TablaDestino" />
            </div>
            <div class="mb-3">
                <SfMultiSelect TValue="List<string>" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnasClave"
                               Placeholder="Columnas clave (requerido si Reemplazar)" FloatLabelType="FloatLabelType.Always" AllowCustomValue="true" />
            </div>
            <div class="mb-3">
                <SfDropDownList TValue="string" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnaWatermark"
                                Placeholder="Columna watermark (opcional)" FloatLabelType="FloatLabelType.Always" AllowFiltering="true" />
                <div class="texto-caption texto-secundario mt-1">
                    Tu SQL debe filtrar con @@watermark (ej. WHERE @@watermark IS NULL OR fecha_mod &gt; @@watermark).
                    Si la columna es fecha, debe ser datetime/datetime2. Usa "Traer columnas" para elegir de la lista.
                </div>
            </div>
            <div class="d-flex gap-3">
                <div class="flex-grow-1 mb-3">
                    <SfDropDownList TValue="int" TItem="Opcion" DataSource="_modos" @bind-Value="_form.ModoEscritura"
                                    Placeholder="Modo de escritura" FloatLabelType="FloatLabelType.Always">
                        <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                    </SfDropDownList>
                </div>
                <div class="flex-grow-1 mb-3">
                    <label class="texto-caption texto-secundario">Filas por lote</label>
                    <SfNumericTextBox TValue="int" @bind-Value="_form.FilasPorLote" Min="1"></SfNumericTextBox>
                </div>
            </div>
            <div class="mb-1">
                <SfCheckBox @bind-Checked="_form.Habilitado" Label="Habilitado"></SfCheckBox>
            </div>
        </div>
    </div>

    <div class="panel mb-4">
        <div class="panel-header">Programacion</div>
        <div class="pa-4">
            <div class="d-flex gap-3 flex-wrap">
                <div class="flex-grow-1 mb-3">
                    <SfDropDownList TValue="int" TItem="Opcion" DataSource="_programaciones" @bind-Value="_form.Programacion"
                                    Placeholder="Programacion" FloatLabelType="FloatLabelType.Always">
                        <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                    </SfDropDownList>
                </div>
                @if (_form.Programacion != 0)
                {
                    <div class="flex-grow-1 mb-3">
                        <SfTextBox Placeholder="Hora del dia (HH:mm)" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.HoraDelDia" />
                    </div>
                }
                @if (_form.Programacion == 2)
                {
                    <div class="flex-grow-1 mb-3">
                        <SfDropDownList TValue="int" TItem="Opcion" DataSource="_diasSemana" @bind-Value="_form.DiaDeLaSemana"
                                        Placeholder="Dia de la semana" FloatLabelType="FloatLabelType.Always">
                            <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                        </SfDropDownList>
                    </div>
                }
                @if (_form.Programacion == 3)
                {
                    <div class="flex-grow-1 mb-3">
                        <label class="texto-caption texto-secundario">Dia del mes (1-28)</label>
                        <SfNumericTextBox TValue="int" @bind-Value="_form.DiaDelMes" Min="1" Max="28"></SfNumericTextBox>
                    </div>
                }
            </div>
        </div>
    </div>

    <div class="panel mb-4">
        <div class="panel-header">Origen</div>
        <div class="pa-4">
            <div class="mb-3">
                <SfDropDownList TValue="Guid?" TItem="DataSourceVista" DataSource="_datasources" @bind-Value="_form.DataSourceId"
                                Placeholder="DataSource *" FloatLabelType="FloatLabelType.Always">
                    <DropDownListFieldSettings Value="Id" Text="Nombre"></DropDownListFieldSettings>
                </SfDropDownList>
            </div>
            <div class="mb-1">
                <SfTextBox Placeholder="SQL *" FloatLabelType="FloatLabelType.Always" Multiline="true" @bind-Value="_form.Sql" />
            </div>
            <div class="mt-2 d-flex align-center gap-2">
                <SfButton CssClass="e-small" OnClick="TraerColumnas" Disabled="@(_form.DataSourceId is null || string.IsNullOrWhiteSpace(_form.Sql))">
                    Traer columnas
                </SfButton>
                @if (_columnasMsg is not null)
                {
                    <span class="texto-caption texto-secundario">@_columnasMsg</span>
                }
            </div>
        </div>
    </div>

    <div class="panel mb-4">
        <div class="panel-header d-flex justify-space-between align-center">
            <span>Transformaciones</span>
            <SfButton CssClass="e-small" OnClick="AgregarTransform">+ Agregar transformacion</SfButton>
        </div>
        <div class="pa-4">
            @if (_form.Transforms.Count == 0)
            {
                <div class="texto-secundario">Sin transformaciones. El origen se escribe tal cual en la tabla destino.</div>
            }
            @for (var i = 0; i < _form.Transforms.Count; i++)
            {
                var t = _form.Transforms[i];
                var idx = i;
                <div class="panel pa-3 mb-3">
                    <div class="d-flex justify-space-between align-center mb-2">
                        <strong>Paso @(idx + 1)</strong>
                        <span class="d-flex gap-1">
                            <SfButton CssClass="e-small" Disabled="@(idx == 0)" OnClick="@(() => Subir(t))">▲</SfButton>
                            <SfButton CssClass="e-small" Disabled="@(idx == _form.Transforms.Count - 1)" OnClick="@(() => Bajar(t))">▼</SfButton>
                            <SfButton CssClass="e-small e-danger e-outline" OnClick="@(() => QuitarTransform(t))">Quitar</SfButton>
                        </span>
                    </div>
                    <div class="mb-2">
                        <SfTextBox Placeholder="Nombre del paso" FloatLabelType="FloatLabelType.Always" @bind-Value="t.Nombre" />
                    </div>
                    <KeyValueEditor Items="t.Rename" Titulo="Rename (renombrar columnas)" ClaveLabel="Columna origen" ValorLabel="Nuevo nombre" />
                    <KeyValueEditor Items="t.Cast" Titulo="Cast (convertir tipos)" ClaveLabel="Columna" ValorLabel="Tipo (int/long/decimal/double/bool/date/string)" />
                    <KeyValueEditor Items="t.Computed" Titulo="Computed (columnas calculadas)" ClaveLabel="Nueva columna" ValorLabel="Expresion (NCalc)" />
                    <div class="mb-1">
                        <SfTextBox Placeholder="Filter (expresion booleana NCalc, opcional)" FloatLabelType="FloatLabelType.Always" @bind-Value="t.Filter" />
                    </div>
                </div>
            }
        </div>
    </div>

    <div class="d-flex gap-2 mb-8">
        <SfButton CssClass="e-primary" OnClick="Guardar" Disabled="_guardando">
            @(_guardando ? "Guardando..." : "Guardar")
        </SfButton>
        <SfButton CssClass="e-flat" OnClick="@(() => Nav.NavigateTo("/pipelines"))">Cancelar</SfButton>
    </div>
}
```

Cambios respecto del original: header→`page-header`; `alert`→`SfMessage`; `<p>Cargando...</p>`→`SfSpinner`; cards Bootstrap→`panel`+`panel-header`+`pa-4`; `.row/.col-md-*`→`d-flex gap-3`+`flex-grow-1`; `form-text`/`form-label small`/`text-muted`→`texto-caption texto-secundario`; `border rounded p-3`→`panel pa-3`; links `btn btn-link`/`btn btn-light`→`SfButton e-flat` con `Nav.NavigateTo`. **La logica (`@code`) y los controles del schema-discovery no cambian.**

- [ ] **Step 2: Reskin de `KeyValueEditor.razor` (markup)**

Reemplazar el markup (lineas 3-25, todo antes de `@code`) por:

```razor
<div class="mb-3">
    <label class="texto-body2" style="font-weight:600;">@Titulo</label>

    @if (Items.Count == 0)
    {
        <div class="texto-caption texto-secundario mb-1">Sin entradas.</div>
    }

    @foreach (var kv in Items)
    {
        <div class="d-flex gap-2 mb-1 align-center">
            <div style="flex:1;">
                <SfTextBox Placeholder="@ClaveLabel" @bind-Value="kv.Clave" />
            </div>
            <div style="flex:1;">
                <SfTextBox Placeholder="@ValorLabel" @bind-Value="kv.Valor" />
            </div>
            <SfButton CssClass="e-small e-danger e-outline" OnClick="@(() => Quitar(kv))" aria-label="Quitar">✕</SfButton>
        </div>
    }

    <SfButton CssClass="e-small" OnClick="Agregar">+ Agregar</SfButton>
</div>
```

- [ ] **Step 3: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 4: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor DataPipeline.Admin/Components/Shared/KeyValueEditor.razor
git commit -m "feat(admin): reskin Editor (paneles, SfSpinner, SfMessage) y KeyValueEditor"
```

---

### Task 9: Error/NotFound + limpieza de Bootstrap

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Error.razor` (quitar `text-danger` de Bootstrap)
- Modify: `DataPipeline.Admin/Components/Pages/NotFound.razor` (envolver en page-header, opcional)
- Delete: `DataPipeline.Admin/wwwroot/lib/bootstrap/` (arbol completo)

**Interfaces:**
- Consumes: `page-title`, `texto-*` (Task 1). Cierra el reskin quitando la dependencia de Bootstrap del repo.

- [ ] **Step 1: Error.razor — reemplazar `text-danger`**

En `DataPipeline.Admin/Components/Pages/Error.razor`:
- `<h1 class="text-danger">Error.</h1>` → `<h1 class="page-title" style="color:var(--error);">Error.</h1>`
- `<h2 class="text-danger">An error occurred while processing your request.</h2>` → `<h2 style="color:var(--error);">An error occurred while processing your request.</h2>`

- [ ] **Step 2: NotFound.razor — encuadrar con el estilo nuevo**

Reemplazar el contenido (despues de las directivas `@page`/`@layout`) por:

```razor
<PageTitle>No encontrado</PageTitle>

<div class="page-header">
    <div>
        <h1 class="page-title">No encontrado</h1>
        <p class="page-subtitle">La pagina que buscas no existe.</p>
    </div>
</div>
```

- [ ] **Step 3: Verificar que ninguna pantalla referencia clases de Bootstrap antes de borrar la lib**

Run (Grep sobre `.razor`): buscar `container-fluid|alert-|btn-link|btn-light|\bcard\b|card-header|card-body|\brow\b|col-md-|form-text|form-label|text-danger|text-muted|fw-semibold`
Expected: sin coincidencias en `DataPipeline.Admin/Components/**/*.razor` (las utilidades `d-flex`, `gap-*`, `mb-*`, `mt-*`, `px-*`, `flex-grow-1`, `flex-wrap` SI estan permitidas — las provee utilities.css).

> Si aparece alguna coincidencia, corregirla en su pantalla antes de borrar la lib. `justify-content-end` residual (si quedo en el dialog de DataSources) reemplazar por `style="display:flex;justify-content:flex-end;"`.

- [ ] **Step 4: Borrar el arbol de Bootstrap**

```bash
cd "D:/Sistemas/MiddleWare"
git rm -r DataPipeline.Admin/wwwroot/lib/bootstrap
```

- [ ] **Step 5: Build limpio**

Run: `dotnet build DataPipeline.Admin/DataPipeline.Admin.csproj --no-incremental`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
cd "D:/Sistemas/MiddleWare"
git add DataPipeline.Admin/Components/Pages/Error.razor DataPipeline.Admin/Components/Pages/NotFound.razor
git commit -m "feat(admin): reskin Error/NotFound y elimina la dependencia de Bootstrap"
```

---

## Verificacion final (manual, a cargo del usuario)

Correr el Admin (alinear `ApiBaseUrl` con la URL https real de la API; credenciales dev del proyecto) y revisar visualmente contra el look de CG:
- Shell: sidebar gradiente, nav con marca/secciones/e-icons, top-row con chip de usuario.
- Home: 3 cards con hover.
- Login: panel centrado con logo y animacion.
- DataSources / Pipelines / Runs: page-header, botones con gradiente, badges de estado, feedback con SfMessage.
- Editor: paneles por seccion, controles del schema-discovery intactos y funcionando (Traer columnas, watermark, columnas clave).

Confirmar en las 3 grillas que no quedaron restos de estilo Bootstrap (alerts, cards) y que el icono de Corridas renderiza (si no, cambiar `e-history`→`e-refresh` en `NavMenu.razor`).
