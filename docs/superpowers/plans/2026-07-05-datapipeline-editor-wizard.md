# Editor de pipelines como wizard de 3 pasos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganizar el editor de pipelines (`Editor.razor`) como un wizard de 3 pasos guiados que sigue el flujo de los datos (origen/columnas → salida/clave → transforms), con el Paso 2 habilitado recien tras "traer columnas".

**Architecture:** Wizard en un solo archivo: se agrega estado de paso activo + gating + navegacion al `@code` existente, y el markup se reorganiza en secciones `@if (_pasoActivo == N)` con un stepper y una barra Atras/Siguiente/Guardar. Todo el `@code` de datos (`_form`, `ConstruirDef`, `Cargar`, `Guardar`, `TraerColumnas`, helpers de transforms) queda intacto — es una reorganizacion de presentacion.

**Tech Stack:** Blazor Server + Syncfusion, C#, .NET 10.

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches. **NO usar `git add -A`**: stagear rutas explicitas.
- Gate de 0 warnings: `dotnet build --no-incremental`.
- **Build-only:** es UI Blazor/Syncfusion; no se escriben unit tests (los escribe el usuario). Verificacion = build limpio + prueba manual.
- Sin cambios de backend/motor: reusa `TraerColumnas` (endpoint `/columnas`) y el guardado actual (`ConstruirDef` + `PipelinesAdminService.GuardarAsync`).
- Un paso visible a la vez. Gate P1→P2 = columnas traidas (`EsquemaOrigen` no vacio). Gate P2→P3 = Tabla destino + (si Reemplazar, ≥1 clave). En **modo edicion** (Id != null) todos los pasos quedan navegables sin gating.
- Razor: literal `@` se escribe `@@`.

---

### Task 1: Wizard de 3 pasos en `Editor.razor`

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor` (markup dentro del `else`, lineas 27-168; y agregados al `@code`)

**Interfaces:**
- Consumes (sin cambios): `_form` (PipelineForm), `NombresColumnas`, `TraerColumnas()`, `AgregarTransform()`/`QuitarTransform()`/`Subir()`/`Bajar()`, `Guardar()`, `_modos`/`_programaciones`/`_diasSemana`, `_datasources`, `_columnasMsg`, `_guardando`.
- Produces: estado de wizard (`_pasoActivo` y helpers de gating/navegacion).

- [ ] **Step 1: Agregar el estado del wizard al `@code`**

En `Editor.razor`, dentro del bloque `@code { ... }`, despues del campo `private string? _columnasMsg;` (linea 221), agregar:

```csharp
    private int _pasoActivo = 1;

    private bool Editando => Id is not null;

    // Gates: P1 requiere columnas traidas; P2 requiere destino + (si Reemplazar) al menos una clave.
    private bool GateP1 => _form.EsquemaOrigen.Count > 0;
    private bool GateP2 => !string.IsNullOrWhiteSpace(_form.TablaDestino)
        && (_form.ModoEscritura != 2 || _form.ColumnasClave.Count > 0);

    // En modo edicion todos los pasos son navegables (el pipeline ya es valido).
    private bool PasoHabilitado(int n) => Editando || n switch
    {
        1 => true,
        2 => GateP1,
        3 => GateP1 && GateP2,
        _ => false
    };

    private bool PuedeAvanzar => Editando || _pasoActivo switch { 1 => GateP1, 2 => GateP2, _ => true };

    private void IrAPaso(int n) { if (PasoHabilitado(n)) _pasoActivo = n; }
    private void Atras() { if (_pasoActivo > 1) _pasoActivo--; }
    private void Siguiente() { if (_pasoActivo < 3 && PuedeAvanzar) _pasoActivo++; }
```

- [ ] **Step 2: Reemplazar el markup por el wizard**

En `Editor.razor`, reemplazar TODO el contenido entre `else {` (linea 25) y su cierre `}` (linea 169) — es decir, las lineas 27-168 (las cuatro tarjetas Generales/Programacion/Origen/Transformaciones + la barra de guardado) — por el siguiente markup. NO tocar `else {` ni su `}` de cierre, ni nada arriba (page-header, error, spinner) ni el `@code`.

```razor
    <div class="panel mb-3">
        <div class="pa-4 d-flex gap-3 align-center flex-wrap">
            <div class="flex-grow-1">
                <SfTextBox Placeholder="Nombre *" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.Nombre" />
            </div>
            <SfCheckBox @bind-Checked="_form.Habilitado" Label="Habilitado"></SfCheckBox>
        </div>
    </div>

    <div class="d-flex align-center gap-2 mb-4 flex-wrap">
        <SfButton CssClass="@(_pasoActivo == 1 ? "e-primary e-small" : "e-flat e-small")" Disabled="@(!PasoHabilitado(1))" OnClick="@(() => IrAPaso(1))">1. Origen y columnas</SfButton>
        <span class="texto-secundario">›</span>
        <SfButton CssClass="@(_pasoActivo == 2 ? "e-primary e-small" : "e-flat e-small")" Disabled="@(!PasoHabilitado(2))" OnClick="@(() => IrAPaso(2))">2. Salida</SfButton>
        <span class="texto-secundario">›</span>
        <SfButton CssClass="@(_pasoActivo == 3 ? "e-primary e-small" : "e-flat e-small")" Disabled="@(!PasoHabilitado(3))" OnClick="@(() => IrAPaso(3))">3. Transformaciones</SfButton>
    </div>

    @if (_pasoActivo == 1)
    {
        <div class="panel mb-4">
            <div class="panel-header">Paso 1 — Origen y columnas</div>
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
                <div class="mt-2 mb-3 d-flex align-center gap-2">
                    <SfButton CssClass="e-small" OnClick="TraerColumnas" Disabled="@(_form.DataSourceId is null || string.IsNullOrWhiteSpace(_form.Sql))">
                        Traer columnas
                    </SfButton>
                    @if (_columnasMsg is not null)
                    {
                        <span class="texto-caption texto-secundario">@_columnasMsg</span>
                    }
                </div>
                @if (_form.EsquemaOrigen.Count > 0)
                {
                    <div class="d-flex flex-wrap gap-2 mb-3">
                        @foreach (var c in _form.EsquemaOrigen)
                        {
                            <span style="border:1px solid #d0d0d0;border-radius:6px;padding:2px 8px;font-size:12px;">@c.Nombre <span class="texto-secundario">@c.Tipo</span></span>
                        }
                    </div>
                    <div class="mb-1">
                        <SfDropDownList TValue="string" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnaWatermark"
                                        Placeholder="Columna watermark (opcional)" FloatLabelType="FloatLabelType.Always" AllowFiltering="true" />
                        <div class="texto-caption texto-secundario mt-1">
                            Tu SQL debe filtrar con @@watermark (ej. WHERE @@watermark IS NULL OR fecha_mod &gt; @@watermark).
                            Si la columna es fecha, debe ser datetime/datetime2.
                        </div>
                    </div>
                }
            </div>
        </div>
    }

    @if (_pasoActivo == 2)
    {
        <div class="panel mb-4">
            <div class="panel-header">Paso 2 — Salida</div>
            <div class="pa-4">
                @if (_form.EsquemaOrigen.Count > 0)
                {
                    <div class="texto-caption texto-secundario mb-1">Columnas del origen</div>
                    <div class="d-flex flex-wrap gap-2 mb-3">
                        @foreach (var n in NombresColumnas)
                        {
                            <span style="border:1px solid #d0d0d0;border-radius:6px;padding:2px 8px;font-size:12px;">@n</span>
                        }
                    </div>
                }
                <div class="mb-3">
                    <SfMultiSelect TValue="List<string>" TItem="string" DataSource="NombresColumnas" @bind-Value="_form.ColumnasClave"
                                   Placeholder="Columnas clave (requerido si Reemplazar)" FloatLabelType="FloatLabelType.Always" AllowCustomValue="true" />
                </div>
                <div class="d-flex gap-3">
                    <div class="flex-grow-1 mb-3">
                        <SfDropDownList TValue="int" TItem="Opcion" DataSource="_modos" @bind-Value="_form.ModoEscritura"
                                        Placeholder="Modo de escritura" FloatLabelType="FloatLabelType.Always">
                            <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                        </SfDropDownList>
                    </div>
                    <div class="flex-grow-1 mb-3">
                        <SfTextBox Placeholder="Tabla destino *" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.TablaDestino" />
                    </div>
                </div>
                <div class="mb-1" style="max-width:220px;">
                    <label class="texto-caption texto-secundario">Filas por lote</label>
                    <SfNumericTextBox TValue="int" @bind-Value="_form.FilasPorLote" Min="1"></SfNumericTextBox>
                </div>
            </div>
        </div>
    }

    @if (_pasoActivo == 3)
    {
        <div class="panel mb-4">
            <div class="panel-header d-flex justify-space-between align-center">
                <span>Paso 3 — Transformaciones (opcional)</span>
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
    }

    <div class="d-flex gap-2 mb-8 align-center">
        <SfButton CssClass="e-flat" OnClick="Atras" Disabled="@(_pasoActivo == 1)">← Atras</SfButton>
        @if (_pasoActivo < 3)
        {
            <SfButton CssClass="e-primary" OnClick="Siguiente" Disabled="@(!PuedeAvanzar)">Siguiente →</SfButton>
            @if (_pasoActivo == 1 && !PuedeAvanzar)
            {
                <span class="texto-caption texto-secundario">Trae las columnas para continuar.</span>
            }
        }
        else
        {
            <SfButton CssClass="e-primary" OnClick="Guardar" Disabled="_guardando">
                @(_guardando ? "Guardando..." : "Guardar")
            </SfButton>
        }
        <SfButton CssClass="e-flat" OnClick="@(() => Nav.NavigateTo("/pipelines"))">Cancelar</SfButton>
    </div>
```

> Notas: el `@code` (form, `ConstruirDef`, `Cargar`, `Guardar`, `TraerColumnas`, `NombresColumnas`, helpers de transforms, listas de opciones) queda **sin cambios** salvo lo agregado en el Step 1. El estilo inline del chip (`border:1px solid #d0d0d0`) es cosmetico; ajustarlo si desentona con el tema del Admin.

- [ ] **Step 3: Build limpio del Admin**

Run: `dotnet build DataPipeline.Admin --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). (Prestar atencion a errores de Razor: `@@watermark` bien escapado, cierres de `@if`, y que no haya markup viejo huerfano fuera de los `@if`.)

- [ ] **Step 4: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "feat(admin): editor de pipelines como wizard de 3 pasos [build-only]"
```

---

### Task 2: Verificacion final

**Files:** (ninguno — solo verificacion)

- [ ] **Step 1: Build completo sin warnings**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Suite completa verde (nada roto)**

Run: `dotnet test --nologo`
Expected: todos verdes (no hay cambios de backend/test; solo confirma que nada se rompio a nivel compilacion de la solucion).

---

## Notas para quien ejecute

- **Verificacion manual (build-only):** en el Admin, crear un pipeline nuevo → el Paso 2 debe estar bloqueado hasta hacer "Traer columnas" en el Paso 1; despues, elegir clave/modo/destino en el Paso 2 y transforms/programacion en el Paso 3; guardar. Editar un pipeline existente → los 3 pasos navegables desde el arranque (incluso uno viejo sin `EsquemaOrigen`).
- **Sin cambios de datos:** el pipeline se guarda exactamente igual que antes (`ConstruirDef` intacto).
- **Diferido / aparte:** split en componentes hijo (se eligio un solo archivo); renovacion integral de la UI del Admin (tarea aparte).
