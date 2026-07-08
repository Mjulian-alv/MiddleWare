# DataPipeline.Admin — Renovacion de UI (reskin) — Design

**Fecha:** 2026-07-08
**Estado:** aprobado (decisiones validadas con el usuario).
**Precede a:** su plan de implementacion.
**Se apoya en:** el Admin actual de ConsultasGenericas (`D:\Sistemas\ConsultasGenericas\ConsultasGenericas.Admin`), que aporta el design system, el shell y los patrones de pantalla.

## Que resuelve y por que

La UI de `DataPipeline.Admin` (Blazor Server + Syncfusion) esta calcada de una version **vieja** del Admin de ConsultasGenericas y quedo desactualizada. El Admin de ConsultasGenericas evoluciono a un design system propio (paleta indigo, `utilities.css`, shell con sidebar en gradiente, patrones de pagina consistentes) que DataPipeline no tiene. Se renueva DataPipeline.Admin **tomando ese look como base**.

Diagnostico de la exploracion: **las pantallas de DataPipeline tienen la logica bien**; la diferencia contra el Admin nuevo es casi toda de **estilos/plantilla** (falta `utilities.css`, `app.css` es la plantilla default de Blazor, el shell y las paginas usan clases de Bootstrap crudas). Por eso el trabajo es un **reskin**, no una reescritura.

## Decisiones (validadas)

| # | Decision | Resolucion |
|---|----------|------------|
| 1 | Enfoque | **Reskin sobre lo existente.** No se toca logica de paginas, servicios HttpClient, auth ni contratos HTTP. Solo markup/estilos/plantilla. |
| 2 | Marca | **Mismo logo que CG (Alvear Sistemas).** Se reusan `wwwroot/brand/*` de CG; wordmark adaptado a "DataPipeline" / "Panel de Administracion". |
| 3 | Bootstrap | **Se quita** el `bootstrap.min.css` completo del `<head>` (CG no lo usa: le alcanza el tema `bootstrap5` de Syncfusion + `utilities.css`). Las clases Bootstrap residuales de las paginas se reemplazan en el repaso por-pantalla. |
| 4 | Alcance de pantallas | Shell (`MainLayout`, `NavMenu`) + Home, Login, DataSources/Index, Pipelines/Index, Pipelines/Editor (+ `KeyValueEditor`), Runs/Index. El Editor **entra en este pase**. |
| 5 | Feature schema-discovery | Los controles nuevos del Editor (dropdown watermark, multiselect clave, boton "Traer columnas") se **respetan tal cual**; solo se les aplica el estilo nuevo. |
| 6 | Tests | El plan **no** incluye tareas de escribir unit tests (los escribe el usuario). Verificacion por build limpio + revision visual manual. |

## No-alcance

- Reorganizar flujos o estructura de pantallas (p. ej. separar Index/Form como hace CG en DataSources) — se conserva la estructura actual de cada pantalla.
- Features nuevas o cambios funcionales.
- Tocar la API, el motor, la persistencia o los servicios del Admin.
- Portar pantallas de CG que no existen en DataPipeline (Endpoints, Users, Dashboards, Envios, Smtp).

## Componentes

### 1. Design system (base)

- **`wwwroot/utilities.css`** (nuevo, copiado de CG): tokens en `:root` (`--brand:#6366f1`, `--brand-2`, `--ink*`, `--bg:#f6f7fb`, `--surface`, `--border`, `--radius`, sombras, curvas de easing), utilidades de display/flex/spacing, grilla de 12 columnas (`.fila-grid`, `.col-*`), `.page-header`/`.page-title`/`.page-subtitle`, `.panel`/`.panel-hover`/`.panel-header`, `.icon-tile`, tipografia auxiliar (`.texto-body2`, `.texto-caption`, `.texto-secundario`), `.tabla-simple`, `.bloque-codigo`, animaciones bajo `prefers-reduced-motion`, y overrides cosmeticos de Syncfusion (`.e-btn` con gradiente en primario, `.e-grid` headers en mayusculas, `.e-badge` pill, `.e-accordion`, `.e-message`).
- **`wwwroot/app.css`** (reemplazado por la version de CG): `Segoe UI`, fondo `#f6f7fb`, links `#6366f1`, resto igual (validation, blazor-error-boundary, etc.).
- **`Components/App.razor`**: en el `<head>`, cargar `utilities.css` + `app.css`; **quitar** `<link ... lib/bootstrap/dist/css/bootstrap.min.css>`. Mantener el tema `bootstrap5` de Syncfusion, `ConsultasGenericas.Admin.styles.css`→`DataPipeline.Admin.styles.css` (ya esta), `ImportMap`, `HeadOutlet`. Cambiar `<html lang="en">`→`"es"` y el favicon a `favicon.ico` (copiado de CG).
- **`wwwroot/brand/`** (nuevo): copiar `logo.png`, `icon.png`, `icon-512.png` y `favicon.ico` de CG.
- Opcional: eliminar el arbol `wwwroot/lib/bootstrap/` una vez que ninguna pantalla dependa de sus clases (ver "Riesgos").

### 2. Shell

- **`Components/Layout/MainLayout.razor`**: adoptar la version de CG — top-row con blur sticky, `justify-space-between`/`align-center` (utilities de CG), y **chip de avatar** con `Session.Username` + boton "Salir" `e-flat e-small`. Conserva el gate de sesion actual (`OnInitialized` → redirect a `/login`) y el `SfDialogProvider` si se adopta `SfDialogService` (ver pantallas). Textos sin tildes en identificadores; el copy visible puede llevar tildes.
- **`Components/Layout/MainLayout.razor.css`**: version de CG — `.page`/`.sidebar` (gradiente `#131129`→`#1e1b4b`), `.top-row` con blur, breakpoints (sidebar 256px sticky en >=641px), `#blazor-error-ui`.
- **`Components/Layout/NavMenu.razor`**: version de CG — `.brand` con `brand-mark` (logo Alvear) + `brand-text` ("DataPipeline" / small "Panel de Administracion"), `nav-section` opcionales, y `NavLink`s con `e-icons`. Items de DataPipeline: Inicio (`e-home`, `href=""` Match All), Origenes (`e-folder`, `datasources`), Pipelines (`e-settings`, `pipelines`), Corridas (`e-history`, `runs`). Con 4 items alcanza una sola seccion "General" o lista plana (a criterio en el plan; default: agrupar Origenes/Pipelines/Corridas bajo "Datos" e Inicio bajo "General").
- **`Components/Layout/NavMenu.razor.css`**: version de CG — `.navbar-toggler`, `.top-row`, `.brand*`, `.nav-section`, `::deep .nav-link` con `e-icons`, estados `active`/`hover`, `.nav-scrollable` responsive.

### 3. Pantalla por pantalla (solo estilos, sin tocar `@code`)

- **`Home.razor`**: reemplazar `<h1>`+`<ul>` por `page-header` + `fila-grid` con 3 cards `panel panel-hover pa-4` (`icon-tile` + titulo + descripcion + "Ir a ..." con `e-arrow-right`), una por Pipelines / Origenes / Corridas, cada card envuelta en `<a href>`.
- **`Login.razor`**: adoptar el layout centrado de CG (fondo `radial-gradient`, `panel pa-8 cg-pop`, logo `brand/logo.png`, titulo + caption). Conservar la logica actual del login (auth local, primer arranque, toggle mostrar/ocultar password). Ajustar solo el markup y clases.
- **`DataSources/Index.razor`**: `page-header` (titulo "Origenes de datos" + subtitulo), boton "Nuevo origen" `e-btn e-primary` con `e-plus`, grilla con columna Activo como **badge** (`e-badge-success`/`e-badge-danger`), feedback con `SfMessage` en vez de `alert`. Conservar el `SfDialog` de alta/edicion y el de confirmacion (o migrar el confirm a `SfDialogService` para alinear con CG — a decidir en el plan; default: conservar los `SfDialog` actuales, solo restilar). Reemplazar `alert alert-*`, `text-danger`, `text-muted` por `SfMessage` / `texto-secundario` / small con color `var(--error)`.
- **`Pipelines/Index.razor`**: `page-header`, boton primario con icono, grilla restilada, badges donde aplique (estado activo/programado). Feedback con `SfMessage`.
- **`Runs/Index.razor`**: `page-header`, grilla restilada, badge por resultado (exito/error). Feedback con `SfMessage`.
- **`Pipelines/Editor.razor`** (el mas grande): `page-header` + secciones envueltas en `panel` (Generales / Programacion / Origen / Transforms). Restilar botones (`e-btn` variantes), inputs Syncfusion (ya son Sf*), y el bloque de transforms (`KeyValueEditor`). **Respetar** los controles del schema-discovery. No tocar la logica de construccion/parseo de `PipelineDefinition`.
- **`Components/Shared/KeyValueEditor.razor`**: restilar filas/botones agregar-quitar-reordenar con utilities; sin cambios de comportamiento.
- **`Error.razor` / `NotFound.razor`**: verificar que se ven bien sin Bootstrap; ajuste minimo si hace falta.

## Riesgos

- **Quitar Bootstrap:** las pantallas actuales usan clases de Bootstrap (`alert`, `alert-success/danger`, `container-fluid`, `text-danger`, `text-muted`, `py-2`, `mb-0`, `d-flex`, `justify-content-between`, `align-items-center`). `utilities.css` de CG cubre las de layout con **otros nombres** (`justify-space-between`, `align-center`) — hay que reemplazar los usos, no solo cambiar el CSS. `alert*` y `text-*` no tienen equivalente en utilities: se reemplazan por `SfMessage` y por `texto-secundario`/inline. **Mitigacion:** el repaso por-pantalla hace estos reemplazos; el build no los detecta (son clases CSS), asi que la verificacion es **visual**. Mientras tanto, `wwwroot/lib/bootstrap/` se puede dejar en el repo hasta confirmar que ninguna pantalla lo necesita, y recien despues borrarlo.
- **Divergencia de utilities:** `MainLayout.razor` de DataPipeline hoy usa `justify-content-between align-items-center` (Bootstrap); la version de CG usa `justify-space-between align-center` (utilities). Al portar el shell se traen las clases correctas.
- **Editor tocado por schema-discovery:** el Editor cambio recientemente (build-only). El reskin debe partir del Editor **actual** (post-`8301846`), no de una version vieja.

## Testing / verificacion

- **Sin unit tests nuevos** (los escribe el usuario). El reskin es de presentacion; no hay logica nueva que testear por unidad.
- **Build limpio:** `dotnet build --no-incremental` con **0 warnings** (gate del proyecto).
- **Revision visual manual:** el usuario corre el Admin y verifica cada pantalla (shell, Home, Login, las 3 grillas, el Editor) contra el look de CG.

## Impacto / archivos

- **Nuevos:** `DataPipeline.Admin/wwwroot/utilities.css`, `wwwroot/brand/{logo,icon,icon-512}.png`, `wwwroot/favicon.ico`, `Components/Layout/MainLayout.razor.css` (reemplazo de contenido), `Components/Layout/NavMenu.razor.css` (reemplazo de contenido).
- **Modificados:** `wwwroot/app.css`, `Components/App.razor`, `Components/Layout/MainLayout.razor`, `Components/Layout/NavMenu.razor`, `Components/Pages/{Home,Login,Error,NotFound}.razor`, `Components/Pages/DataSources/Index.razor`, `Components/Pages/Pipelines/{Index,Editor}.razor`, `Components/Pages/Runs/Index.razor`, `Components/Shared/KeyValueEditor.razor`.
- **Eventual (fuera de este pase o al final):** borrar `wwwroot/lib/bootstrap/`.

## Convenciones

- Identificadores tecnicos en ingles; dominio/props/copy en castellano (el copy visible lleva tildes; los identificadores no). Ver `[[datapipeline-project]]`.
- Commits directos a `main`, rutas explicitas (nada de `git add -A`). Ver `[[commit-workflow-main]]`.
- Gate de 0 warnings con `dotnet build --no-incremental`. Ver `[[verify-zero-warnings-clean-build]]`.
- Planes sin tareas de unit tests. Ver `[[planes-sin-unit-tests]]`.
