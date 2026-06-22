# DataPipeline — Fase 3E (Editor visual de pipelines) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. UI del Admin **build-only** (decision del usuario, se mantiene). No hay cambios en la API en esta rebanada (reusa /api/pipelines y /api/datasources existentes).

**Goal:** Crear/editar pipelines visualmente desde el Admin, sin tocar JSON: datos generales + schedule, un origen (DataSource + SQL), y **multiples pasos de transformacion en cadena** (ColumnMapTransform: rename/cast/computed via filas clave/valor + filter), con agregar/quitar/reordenar. Genera el `PipelineDefinition` y lo guarda por `POST /api/pipelines`.

**Architecture:** Pagina `Pipelines/Editor.razor` (`/pipelines/nuevo` y `/pipelines/editar/{id}`) que arma un `PipelineForm` y lo mapea a `PipelineDefinition` (Pasos = [origen, ...transforms]; `StepDefinition.Configuracion` como `Dictionary<string,JsonElement>` construido con `System.Text.Json`). Componente reusable `KeyValueEditor` para los mapas. Carga via `PipelinesAdminService.ObtenerPorIdAsync`; guarda via `GuardarAsync`. El dropdown de origenes usa `DataSourcesAdminService`.

**Tech Stack:** Blazor Server + Syncfusion (Inputs, DropDowns, Buttons), System.Text.Json.

**Spec:** umbrella 3E. Decision usuario: multiples transforms en cadena; mapas como filas clave/valor.

## Global Constraints
- net10.0; build pristine (`dotnet build --no-incremental`); sin tildes/ñ en identificadores.
- Commits directo a `main`, uno por tarea, `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Round-trip JSON: origen step `Conector="SqlServerSource"`, `Configuracion={dataSourceId, sql}`; transform step `Conector="ColumnMapTransform"`, `Configuracion={rename?, cast?, computed?, filter?}` (mapas como objeto JSON, filter como string). Enums serializan como int (consistente con la API).

---

### Task 1: Modelos + `KeyValueEditor` + usings

**Files:** Create `DataPipeline.Admin/Models/KV.cs`, `DataPipeline.Admin/Components/Shared/KeyValueEditor.razor`; Modify `Components/_Imports.razor` (+ `System.Text.Json`, `DataPipeline.Admin.Models`, `DataPipeline.Admin.Components.Shared`).

- [ ] `KV { string Clave; string Valor; }`. `KeyValueEditor` (params: `List<KV> Items`, `Titulo`, `ClaveLabel`, `ValorLabel`) con filas SfTextBox + boton quitar por fila + boton "+ Agregar". Build limpio. Commit `feat(admin): KeyValueEditor + modelo KV`.

---

### Task 2: `Pipelines/Editor.razor` + navegacion desde la lista

**Files:** Create `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor`; Modify `Components/Pages/Pipelines/Index.razor` (boton "Nuevo" -> `/pipelines/nuevo`; "Editar" por fila -> `/pipelines/editar/{id}`).

**Interfaces:**
- Consume `PipelinesAdminService` (ObtenerPorIdAsync/GuardarAsync), `DataSourcesAdminService` (ObtenerTodosAsync para el dropdown), `PipelineDefinition`/`StepDefinition`/`TipoPaso`/`TipoProgramacion`/`ModoEscritura`.
- `PipelineForm` (Admin) -> `PipelineDefinition`:
  - Generales: Nombre (req), Habilitado, TablaDestino (req), ModoEscritura (Agregar/Reemplazar; se omite Combinar porque el store no lo soporta), FilasPorLote.
  - Schedule: Programacion (BajoDemanda/Diaria/Semanal/Mensual); HoraDelDia si Diaria/Semanal/Mensual; DiaDeLaSemana si Semanal; DiaDelMes si Mensual.
  - Origen: DataSourceId (dropdown), Sql.
  - Transforms: lista ordenada de `TransformForm { Nombre; List<KV> Rename/Cast/Computed; string? Filter }` con agregar/quitar/subir/bajar.

- [ ] **Pasos:**
  - Construir `PipelineDefinition` al guardar: origen `StepDefinition{Conector="SqlServerSource", Configuracion={dataSourceId, sql}}` + por cada transform `StepDefinition{Conector="ColumnMapTransform", Configuracion={rename?,cast?,computed?,filter?}}`. Mapas vacios y filter vacio se omiten. Valores via `JsonSerializer.SerializeToElement(...)`.
  - Cargar (editar): parsear `Pasos` -> form (origen: dataSourceId/sql; transforms: leer mapas-objeto -> List<KV>, filter string).
  - Validacion: Nombre y TablaDestino obligatorios; origen requiere DataSourceId + Sql. Errores cerca del campo; feedback de guardar con loading; al guardar OK volver a `/pipelines`.
  - UX (ui-ux-pro-max forms): labels visibles, required indicators, error-placement, submit-feedback, disabled durante guardado, un CTA primario (Guardar), agrupar campos por seccion (Generales / Programacion / Origen / Transformaciones).
  - En `Pipelines/Index.razor`: el boton "Nuevo origen"... (no aplica) — agregar boton "Nuevo pipeline" -> `/pipelines/nuevo`, y accion "Editar" por fila -> `/pipelines/editar/{id}` (NavigationManager).
  - Build `dotnet build --no-incremental` limpio. Commit `feat(admin): editor visual de pipelines (/pipelines/nuevo y /editar/{id})`.

## Self-Review
- Cobertura 3E: generales + schedule + origen + multiples transforms en cadena (Task 2) ✅; mapas clave/valor (Task 1) ✅; genera/edita el JSON sin tocarlo (Task 2) ✅.
- Round-trip: construccion y parseo de `Configuracion` simetricos con TransformFactory/SourceFactory + hydrator (origen por dataSourceId).
- Build-only; prueba funcional manual. Fuera de alcance: ejecucion encadenada real de `CorrerDespuesDe` (Fase 4); por eso el editor no expone RunAfter en v1 (se puede sumar luego).
