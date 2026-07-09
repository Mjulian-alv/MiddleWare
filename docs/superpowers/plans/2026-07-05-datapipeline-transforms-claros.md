# Paso 3 (Transformaciones) claro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hacer claras y bien definidas las 4 operaciones de transformacion del editor (renombrar, convertir tipo, columna calculada, filtrar): rotuladas, con el tipo de cast por dropdown, columnas por combo, y ayuda de sintaxis NCalc — sin tocar el motor.

**Architecture:** Se extiende el componente compartido `KeyValueEditor.razor` con parametros opcionales (descripcion, clave-combo, valor-dropdown) y se rediseña el markup del bloque de transformacion en `Editor.razor` para usarlo con rotulos, orden fijo visible, dropdown de tipos y ayuda NCalc. El `@code` de datos y el motor no cambian.

**Tech Stack:** Blazor Server + Syncfusion, C#, .NET 10.

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches. **NO usar `git add -A`**: stagear rutas explicitas.
- Gate de 0 warnings: `dotnet build --no-incremental`.
- **Build-only:** es UI Blazor/Syncfusion; no se escriben unit tests (los escribe el usuario). Verificacion = build limpio + prueba manual.
- Sin cambios de motor (`ColumnMapTransform`) ni de datos guardados. El cast dropdown produce los mismos strings que hoy (`int/long/decimal/double/bool/date/string`).
- Razor: literal `@` se escribe `@@`.

---

### Task 1: Extender `KeyValueEditor`

**Files:**
- Modify: `DataPipeline.Admin/Components/Shared/KeyValueEditor.razor`

**Interfaces:**
- Produces: parametros nuevos `Descripcion` (string?), `ClaveOpciones` (List<string>?), `ValorOpciones` (List<string>?). Si `ClaveOpciones`/`ValorOpciones` vienen, ese input pasa de textbox a `SfComboBox`. Consumido por Task 2.

- [ ] **Step 1: Reemplazar el contenido de `KeyValueEditor.razor`**

Reemplazar TODO el archivo `DataPipeline.Admin/Components/Shared/KeyValueEditor.razor` por:

```razor
@* Editor de un mapa clave/valor (rename/cast/computed). Sin JSON: filas con agregar/quitar. *@

<div class="mb-3">
    <label class="texto-body2" style="font-weight:600;">@Titulo</label>
    @if (!string.IsNullOrEmpty(Descripcion))
    {
        <div class="texto-caption texto-secundario mb-1">@Descripcion</div>
    }

    @if (Items.Count == 0)
    {
        <div class="texto-caption texto-secundario mb-1">Sin entradas.</div>
    }

    @foreach (var kv in Items)
    {
        <div class="d-flex gap-2 mb-1 align-center">
            <div style="flex:1;">
                @if (ClaveOpciones is not null)
                {
                    <SfComboBox TValue="string" TItem="string" DataSource="ClaveOpciones" @bind-Value="kv.Clave"
                                Placeholder="@ClaveLabel" AllowCustom="true" AllowFiltering="true" />
                }
                else
                {
                    <SfTextBox Placeholder="@ClaveLabel" @bind-Value="kv.Clave" />
                }
            </div>
            <div style="flex:1;">
                @if (ValorOpciones is not null)
                {
                    <SfComboBox TValue="string" TItem="string" DataSource="ValorOpciones" @bind-Value="kv.Valor"
                                Placeholder="@ValorLabel" AllowCustom="false" />
                }
                else
                {
                    <SfTextBox Placeholder="@ValorLabel" @bind-Value="kv.Valor" />
                }
            </div>
            <SfButton CssClass="e-small e-danger e-outline" OnClick="@(() => Quitar(kv))" aria-label="Quitar">✕</SfButton>
        </div>
    }

    <SfButton CssClass="e-small" OnClick="Agregar">+ Agregar</SfButton>
</div>

@code {
    [Parameter] public List<KV> Items { get; set; } = new();
    [Parameter] public string Titulo { get; set; } = "";
    [Parameter] public string? Descripcion { get; set; }
    [Parameter] public string ClaveLabel { get; set; } = "Clave";
    [Parameter] public string ValorLabel { get; set; } = "Valor";
    [Parameter] public List<string>? ClaveOpciones { get; set; }
    [Parameter] public List<string>? ValorOpciones { get; set; }

    private void Agregar() => Items.Add(new KV());
    private void Quitar(KV kv) => Items.Remove(kv);
}
```

> `SfComboBox` vive en `Syncfusion.Blazor.DropDowns` (mismo namespace que `SfDropDownList`/`SfMultiSelect` que ya se usan), asi que resuelve por los `@using` existentes. `AllowCustom="true"` en la clave permite escribir una columna que no este en la lista (renombrada/calculada de otro bloque); `AllowCustom="false"` en el valor restringe a las opciones (tipos de cast).

- [ ] **Step 2: Build limpio del Admin**

Run: `dotnet build DataPipeline.Admin --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). (Los usos actuales de `KeyValueEditor` — sin los parametros nuevos — siguen compilando: los nuevos son opcionales y caen al textbox.)

- [ ] **Step 3: Commit**

```bash
git add DataPipeline.Admin/Components/Shared/KeyValueEditor.razor
git commit -m "feat(admin): KeyValueEditor con descripcion, clave-combo y valor-dropdown opcionales"
```

---

### Task 2: Rediseñar el bloque de transformacion en `Editor.razor`

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor` (el markup del bloque en Paso 3, lineas 142-160; y una constante en `@code`)

**Interfaces:**
- Consumes: `KeyValueEditor` con `Descripcion`/`ClaveOpciones`/`ValorOpciones` (Task 1); `NombresColumnas` (ya existe en el `@code`); `t.Rename`/`t.Cast`/`t.Computed`/`t.Filter` (modelo `TransformForm`, sin cambios).

- [ ] **Step 1: Agregar la constante de tipos de cast al `@code`**

En `Editor.razor`, dentro de `@code { ... }`, junto a las otras listas de opciones (despues de `_diasSemana`), agregar:

```csharp
    private readonly List<string> _tiposCast = new() { "int", "long", "decimal", "double", "bool", "date", "string" };
```

- [ ] **Step 2: Reemplazar el markup del bloque de transformacion**

En `Editor.razor`, reemplazar el bloque de la tarjeta por-transform — las lineas 142-160, que hoy son:

```razor
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
```

por:

```razor
                    <div class="panel pa-3 mb-3">
                        <div class="d-flex justify-space-between align-center mb-1">
                            <strong>Bloque de transformacion @(idx + 1)</strong>
                            <span class="d-flex gap-1">
                                <SfButton CssClass="e-small" Disabled="@(idx == 0)" OnClick="@(() => Subir(t))">▲</SfButton>
                                <SfButton CssClass="e-small" Disabled="@(idx == _form.Transforms.Count - 1)" OnClick="@(() => Bajar(t))">▼</SfButton>
                                <SfButton CssClass="e-small e-danger e-outline" OnClick="@(() => QuitarTransform(t))">Quitar</SfButton>
                            </span>
                        </div>
                        <div class="texto-caption texto-secundario mb-3">Orden: renombrar → convertir tipo → calcular → filtrar</div>
                        <div class="mb-3">
                            <SfTextBox Placeholder="Nombre del bloque" FloatLabelType="FloatLabelType.Always" @bind-Value="t.Nombre" />
                        </div>
                        <KeyValueEditor Items="t.Rename" Titulo="Renombrar columnas" Descripcion="Cambia el nombre de una columna."
                                        ClaveLabel="Columna" ValorLabel="Nuevo nombre" ClaveOpciones="NombresColumnas" />
                        <KeyValueEditor Items="t.Cast" Titulo="Convertir tipo" Descripcion="Convierte el valor de una columna al tipo elegido."
                                        ClaveLabel="Columna" ValorLabel="Tipo" ClaveOpciones="NombresColumnas" ValorOpciones="_tiposCast" />
                        <KeyValueEditor Items="t.Computed" Titulo="Columna calculada" Descripcion="Crea una columna nueva con una expresion (ej. precio * 1.21)."
                                        ClaveLabel="Nueva columna" ValorLabel="Expresion" />
                        <div class="mb-2">
                            <label class="texto-body2" style="font-weight:600;">Filtrar filas</label>
                            <div class="texto-caption texto-secundario mb-1">Solo pasan las filas donde la expresion es verdadera (ej. monto &gt; 0).</div>
                            <SfTextBox Placeholder="Expresion booleana (opcional)" @bind-Value="t.Filter" />
                        </div>
                        <div class="pa-2 mb-1" style="border:0.5px solid #d0d0d0;border-radius:6px;">
                            <div class="texto-caption texto-secundario">
                                <strong>Sintaxis (NCalc):</strong>
                                operadores <code>+ - * / &gt; &lt; == != and or not</code> ·
                                condicional <code>if(cond, a, b)</code> ·
                                funciones <code>Round, Abs, Max, Min, Floor, Ceiling</code>.
                                <a href="https://github.com/ncalc/ncalc/wiki/Functions" target="_blank" rel="noopener">Ver funciones</a>
                            </div>
                        </div>
                    </div>
```

> No se toca el `@code` de datos (`TransformForm`, `ConstruirDef`, `Cargar`, los helpers `AgregarTransform`/`Subir`/`Bajar`/`QuitarTransform`). Solo se agrega `_tiposCast` (Step 1) y se reemplaza este markup. El estilo inline del recuadro de ayuda es cosmetico; ajustarlo si desentona con el tema.

- [ ] **Step 3: Build limpio del Admin**

Run: `dotnet build DataPipeline.Admin --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). (Watch: `@@` no hace falta aca — no hay `@` literal en este markup salvo dentro de `<code>` que es texto plano; los `&gt;`/`&lt;` estan escapados como HTML.)

- [ ] **Step 4: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "feat(admin): bloque de transformacion claro (4 operaciones rotuladas, tipos por dropdown, ayuda NCalc) [build-only]"
```

---

### Task 3: Verificacion final

**Files:** (ninguno — solo verificacion)

- [ ] **Step 1: Build completo sin warnings**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Suite completa verde (nada roto)**

Run: `dotnet test --nologo`
Expected: todos verdes (no hay cambios de backend/test; confirma que nada se rompio a nivel compilacion).

---

## Notas para quien ejecute

- **Verificacion manual (build-only):** en el editor, Paso 3, agregar un bloque; confirmar que las 4 operaciones estan rotuladas y explicadas, que el tipo de cast es un dropdown de los 7 tipos, que la columna es un combo con las columnas del Paso 1 (y admite escribir una custom), y que el recuadro de ayuda NCalc se ve. Guardar y reabrir un pipeline con transforms existente → debe cargar igual y correr igual.
- **Sin cambios de datos/motor:** el cast dropdown produce los mismos strings; `ColumnMapTransform` no cambia.
- **Diferido / aparte:** operaciones con nombre (trim/concat/formato de fecha); validacion en vivo de NCalc; columnas post-transform reales en los combos de bloques encadenados; renovacion integral de la UI del Admin.
