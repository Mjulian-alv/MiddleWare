# DataPipeline — Origenes de archivo (CSV/XLSX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar dos origenes nuevos a DataPipeline — `Csv` y `Xlsx` — que leen archivos de una carpeta de red, cargan incrementalmente por columna watermark, y mueven cada archivo procesado a `\procesados`, para poder ingestar exports de un ERP viejo sin conexion SQL directa.

**Architecture:** `FileSourceBase` (abstracta, `ISource`) concentra el descubrimiento de archivos, el orden, el filtrado/tipado de la columna watermark y el movido a `\procesados`; `CsvSource` (CsvHelper) y `XlsxSource` (ClosedXML) solo implementan "leer encabezado" y "leer filas" de su formato. El resto de la cadena (`SourceFactory`, `PipelineHydrator`, `RecordBatcher`, `PipelineRunner`) se extiende sin romper el camino de `SqlServerSource` existente.

**Tech Stack:** .NET 10, C#, CsvHelper, ClosedXML, EF Core (SQLite, almacen A), xUnit. Blazor Server + Syncfusion (Admin).

**Spec:** `docs/superpowers/specs/2026-09-03-datapipeline-origenes-archivo-design.md`.

## Global Constraints

- Identificadores tecnicos en ingles; dominio/propiedades en castellano, sin tildes ni ñ en identificadores.
- Commits directos a `main`, sin branches ni remoto. **NO usar `git add -A`**: stagear rutas explicitas.
- Gate de 0 warnings: verificar con `dotnet build --no-incremental`.
- **Convencion de trazabilidad (obligatoria desde ahora, `~/.claude/CLAUDE.md`):** todo bloque de codigo agregado o modificado lleva un comentario:
  ```
  Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - <descripcion corta>
  ```
  Para esta tarea: **TAREA = 00014, Linea = 1** (siempre los mismos dos valores en todo el plan). Formato segun lenguaje: `//` en C#, `--` en SQL/DDL embebido, `@* *@` en Razor. En un archivo **nuevo** va arriba de todo (antes del primer `using`/`namespace`). En una **edicion**, inmediatamente encima del bloque que cambia (no hace falta repetirlo en cada linea, uno por bloque logico). **Cada task de este plan que toca codigo indica explicitamente donde va el comentario; no se omite en ningun paso.**
- `MetadataSchemaInitializer.cs` (no las migraciones EF) es la fuente de verdad del esquema en runtime — las migraciones EF ya no se aplican (decision de la Fase "esquema idempotente"). Este plan **no genera migraciones EF nuevas**, solo edita el inicializador.
- Persistencia via EF usa convenciones por defecto para propiedades escalares simples (`string?`, `bool`) — no hace falta tocar `MetadataDbContext.OnModelCreating` para los campos nuevos (son escalares, no colecciones).
- Los tests existentes de `IConnectionTester`/`ISchemaDiscoverer` (`tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs`) implementan esas interfaces con fakes propios: agregar un metodo a la interfaz **rompe la compilacion de esos fakes** hasta que se actualicen — la Task 6 lo hace explicito.

---

### Task 1: Core + Persistencia — campos de archivo en `DataSource` y `PipelineDefinition`

**Files:**
- Modify: `DataPipeline.Core/DataSources/DataSource.cs`
- Modify: `DataPipeline.Core/Pipelines/PipelineDefinition.cs`
- Modify: `DataPipeline.Persistence/MetadataSchemaInitializer.cs`
- Test: `tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs`
- Test: `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`

**Interfaces:**
- Produces: `TipoDataSource { SqlServer, Csv, Xlsx }`; `DataSource.RutaCarpeta/PatronArchivo/Separador/Encoding/TieneEncabezado/HojaExcel`; `PipelineDefinition.ColumnaWatermarkTipo`. Consumido por todas las tasks siguientes.

- [ ] **Step 1: Escribir los tests de persistencia (fallan por no existir las propiedades)**

En `tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs`, agregar (despues de `Delete_borra_y_devuelve_true_o_false`):

```csharp
    [Fact]
    public async Task Persiste_los_campos_de_archivo_para_Csv_y_Xlsx()
    {
        using var ctx = NuevoCtx();
        var store = new EfDataSourceStore(ctx);
        var csv = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "ventas-csv", Tipo = TipoDataSource.Csv,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.csv",
            Separador = ";", Encoding = "Windows-1252", TieneEncabezado = true,
            Activo = true, CreatedAt = DateTime.Now
        };
        var xlsx = new DataSource
        {
            Id = Guid.NewGuid(), Nombre = "ventas-xlsx", Tipo = TipoDataSource.Xlsx,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.xlsx", HojaExcel = "Datos",
            Activo = true, CreatedAt = DateTime.Now
        };
        await store.UpsertAsync(csv, CancellationToken.None);
        await store.UpsertAsync(xlsx, CancellationToken.None);

        var leidoCsv = await store.GetByIdAsync(csv.Id, CancellationToken.None);
        var leidoXlsx = await store.GetByIdAsync(xlsx.Id, CancellationToken.None);

        Assert.Equal(TipoDataSource.Csv, leidoCsv!.Tipo);
        Assert.Equal(@"\\erp\exports", leidoCsv.RutaCarpeta);
        Assert.Equal("VENTAS_*.csv", leidoCsv.PatronArchivo);
        Assert.Equal(";", leidoCsv.Separador);
        Assert.Equal("Windows-1252", leidoCsv.Encoding);
        Assert.True(leidoCsv.TieneEncabezado);

        Assert.Equal(TipoDataSource.Xlsx, leidoXlsx!.Tipo);
        Assert.Equal("Datos", leidoXlsx.HojaExcel);
    }
```

En `tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs`, agregar (despues de cualquier test existente, por ejemplo al final de la clase antes de la llave de cierre):

```csharp
    [Fact]
    public async Task Pipeline_persiste_ColumnaWatermarkTipo()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var def = Def("con-tipo-watermark", null);
        def.ColumnaWatermark = "fecha_mod";
        def.ColumnaWatermarkTipo = "date";
        await store.UpsertAsync(def, CancellationToken.None);

        var leido = await store.GetByNombreAsync("con-tipo-watermark", CancellationToken.None);

        Assert.Equal("date", leido!.ColumnaWatermarkTipo);
    }
```

- [ ] **Step 2: Correr los tests — deben fallar (no compila)**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: FAIL — error de compilacion, `DataSource` no tiene `RutaCarpeta`/etc., `PipelineDefinition` no tiene `ColumnaWatermarkTipo`, `TipoDataSource` no tiene `Csv`/`Xlsx`.

- [ ] **Step 3: Agregar los campos a `DataSource.cs`**

Reemplazar todo el archivo `DataPipeline.Core/DataSources/DataSource.cs` por:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): nuevos tipos y
// campos de configuracion; reemplaza el FilePath sin uso (comentado "futuro (Excel)").
namespace DataPipeline.Core.DataSources;

/// <summary>Tipo de origen de datos.</summary>
public enum TipoDataSource { SqlServer, Csv, Xlsx }

/// <summary>Conexion nombrada reusable (almacen A). Secretos encriptados en reposo.</summary>
public sealed class DataSource
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public TipoDataSource Tipo { get; set; }
    public string? ConnectionString { get; set; } // encriptado
    public string? Username { get; set; }          // encriptado
    public string? Password { get; set; }          // encriptado
    public string? BaseUrl { get; set; }           // futuro (RestApi)

    /// <summary>Carpeta de red donde se dejan los exports. Solo Csv/Xlsx.</summary>
    public string? RutaCarpeta { get; set; }

    /// <summary>Glob para filtrar archivos (ej. "VENTAS_*.csv"). Vacio = "*.csv"/"*.xlsx" segun Tipo.</summary>
    public string? PatronArchivo { get; set; }

    /// <summary>Separador de campos. Solo Csv. Vacio = ",".</summary>
    public string? Separador { get; set; }

    /// <summary>Encoding del archivo ("UTF-8"/"Windows-1252"). Solo Csv. Vacio = "UTF-8".</summary>
    public string? Encoding { get; set; }

    /// <summary>Si la primera fila es encabezado. Solo Csv.</summary>
    public bool TieneEncabezado { get; set; } = true;

    /// <summary>Nombre de la hoja a leer. Solo Xlsx. Vacio = primera hoja del libro.</summary>
    public string? HojaExcel { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Agregar `ColumnaWatermarkTipo` a `PipelineDefinition.cs`**

En `DataPipeline.Core/Pipelines/PipelineDefinition.cs`, agregar el comentario y la propiedad justo despues de `UltimoWatermark`:

```csharp
    /// <summary>Ultimo maximo visto de <see cref="ColumnaWatermark"/> (persistido tras cada corrida exitosa).</summary>
    public string? UltimoWatermark { get; set; }

    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): el archivo no
    // tiene tipos como SQL Server; sin esto, comparar una fecha o un numero como texto compara mal
    // ("9" > "10"). Uno de int/long/decimal/double/bool/date/string (mismos 7 valores que el cast
    // de Paso 3). Ignorado para SqlServer (ADO.NET ya entrega valores tipados).
    public string? ColumnaWatermarkTipo { get; set; }

    public int FilasPorLote { get; set; } = 5_000;
```

- [ ] **Step 5: Actualizar `MetadataSchemaInitializer.cs`**

En `DataPipeline.Persistence/MetadataSchemaInitializer.cs`, agregar el comentario arriba de la sentencia `CREATE TABLE "Origenes"` y sumar las 6 columnas nuevas antes de `"Activo"`:

```csharp
        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): 6 columnas
        // nuevas en Origenes + ColumnaWatermarkTipo en Pipelines. FilePath queda en la tabla (columna
        // huerfana inofensiva) para no tocar bases existentes; ya no se usa desde el C#.
        """
        CREATE TABLE IF NOT EXISTS "Origenes" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Origenes" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Tipo"             TEXT    NOT NULL,
            "ConnectionString" TEXT    NULL,
            "Username"         TEXT    NULL,
            "Password"         TEXT    NULL,
            "BaseUrl"          TEXT    NULL,
            "FilePath"         TEXT    NULL,
            "RutaCarpeta"      TEXT    NULL,
            "PatronArchivo"    TEXT    NULL,
            "Separador"        TEXT    NULL,
            "Encoding"         TEXT    NULL,
            "TieneEncabezado"  INTEGER NOT NULL DEFAULT 1,
            "HojaExcel"        TEXT    NULL,
            "Activo"           INTEGER NOT NULL,
            "CreatedAt"        TEXT    NOT NULL,
            "UpdatedAt"        TEXT    NULL
        )
        """,
```

Y en la `CREATE TABLE "Pipelines"`, agregar `"ColumnaWatermarkTipo"` despues de `"UltimoWatermark"`:

```csharp
            "ColumnaWatermark" TEXT    NULL,
            "UltimoWatermark"  TEXT    NULL,
            "ColumnaWatermarkTipo" TEXT NULL,
            "FilasPorLote"     INTEGER NOT NULL
```

Y al final del arreglo `Sentencias`, despues de la ultima linea `ALTER TABLE "Pipelines" ADD COLUMN "UltimoWatermark" ...`, agregar (auto-cura de bases existentes):

```csharp
        """ALTER TABLE "Origenes"  ADD COLUMN "RutaCarpeta"          TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "PatronArchivo"        TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "Separador"            TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "Encoding"             TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "TieneEncabezado"      INTEGER NOT NULL DEFAULT 1""",
        """ALTER TABLE "Origenes"  ADD COLUMN "HojaExcel"            TEXT    NULL""",
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnaWatermarkTipo" TEXT    NULL""",
```

- [ ] **Step 6: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Persistence.Tests --nologo`
Expected: PASS (todos, incluidos los 2 nuevos).

- [ ] **Step 7: Build limpio de la solucion completa**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). (Otros proyectos que usaban `DataSource.FilePath` — `DataPipeline.Api/Controllers/DataSourcesController.cs`, `DataPipeline.Admin` — van a fallar aca; se arreglan en las tasks 8/9. Si el build falla SOLO por esos dos, es esperado; anotarlo y seguir.)

- [ ] **Step 8: Commit**

```bash
git add DataPipeline.Core/DataSources/DataSource.cs DataPipeline.Core/Pipelines/PipelineDefinition.cs DataPipeline.Persistence/MetadataSchemaInitializer.cs tests/DataPipeline.Persistence.Tests/DataSourceRepoTests.cs tests/DataPipeline.Persistence.Tests/RepositoriosTests.cs
git commit -m "$(cat <<'EOF'
feat(core): TipoDataSource.Csv/Xlsx + campos de archivo + ColumnaWatermarkTipo

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

> Nota: este commit deja el build roto en `DataPipeline.Api`/`DataPipeline.Admin` (usan `DataSource.FilePath`, eliminado). Se arregla en Task 8 (Api) y Task 9 (Admin). Es el unico punto del plan donde el build de la solucion completa no esta limpio entre tasks; si preferis no commitear un build roto, aplica Task 1 y Task 8 en el mismo checkpoint antes de commitear — pero seguí el orden de abajo si vas task-por-task con revision intermedia.

---

### Task 2: Connectors — extraer `TipoValorConverter` compartido

**Files:**
- Create: `DataPipeline.Connectors/TipoValorConverter.cs`
- Modify: `DataPipeline.Connectors/Transforms/ColumnMapTransform.cs`
- Test: `tests/DataPipeline.Tests/ColumnMapTransformTests.cs`

**Interfaces:**
- Produces: `internal static class DataPipeline.Connectors.TipoValorConverter { static object? Convertir(object val, string tipo) }`. Consumido por `ColumnMapTransform` (ya) y por `FileSourceBase` (Task 3).

**Motivo:** `ColumnMapTransform` ya resuelve exactamente esta conversion para el cast de Paso 3 (7 tipos: int/long/decimal/double/bool/date/string); `FileSourceBase` necesita la misma logica para tipar la columna watermark antes de compararla. Se extrae para no duplicarla (DRY).

- [ ] **Step 1: Escribir el test que fija el contrato del tipo compartido (falla por no existir)**

En `tests/DataPipeline.Tests/ColumnMapTransformTests.cs`, agregar (dentro de la clase, junto a los demas `[Fact]`):

```csharp
    [Fact]
    public void TipoValorConverter_es_usado_por_el_cast_existente()
    {
        // Regresion: el cast de Paso 3 sigue funcionando igual despues de extraer la conversion.
        Assert.Equal(42, DataPipeline.Connectors.TipoValorConverter.Convertir("42", "int"));
        Assert.Equal(3.5m, DataPipeline.Connectors.TipoValorConverter.Convertir("3.5", "decimal"));
        Assert.Equal(new DateTime(2026, 1, 5), DataPipeline.Connectors.TipoValorConverter.Convertir("2026-01-05", "date"));
    }
```

- [ ] **Step 2: Correr el test — debe fallar (no existe `TipoValorConverter`)**

Run: `dotnet test tests/DataPipeline.Tests --filter TipoValorConverter_es_usado_por_el_cast_existente --nologo`
Expected: FAIL — error de compilacion, el tipo no existe.

- [ ] **Step 3: Crear `TipoValorConverter.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Extraido de ColumnMapTransform.Convertir para
// reusarlo al tipar la columna watermark de los origenes de archivo (Csv/Xlsx).
using System.Globalization;

namespace DataPipeline.Connectors;

/// <summary>Convierte un valor crudo a uno de los 7 tipos soportados por el cast de Paso 3.</summary>
internal static class TipoValorConverter
{
    public static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch
    {
        "int"     => Convert.ToInt32(val, CultureInfo.InvariantCulture),
        "long"    => Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "decimal" => Convert.ToDecimal(val, CultureInfo.InvariantCulture),
        "double"  => Convert.ToDouble(val, CultureInfo.InvariantCulture),
        "bool"    => Convert.ToBoolean(val, CultureInfo.InvariantCulture),
        "date"    => DateTime.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        "string"  => Convert.ToString(val, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo de cast no soportado: '{tipo}'.")
    };
}
```

- [ ] **Step 4: Cambiar `ColumnMapTransform.cs` para usar el tipo compartido**

En `DataPipeline.Connectors/Transforms/ColumnMapTransform.cs`, cambiar la linea (dentro de `AplicarCast`):

```csharp
            fila[col] = Convertir(val, tipo);
```

por:

```csharp
            // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Conversion extraida a TipoValorConverter (compartida con FileSourceBase).
            fila[col] = TipoValorConverter.Convertir(val, tipo);
```

Y **eliminar** el metodo privado `Convertir` al final de la clase (lineas 106-116 del archivo original: desde `private static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch` hasta el `};` que lo cierra).

- [ ] **Step 5: Correr TODOS los tests de `DataPipeline.Tests` — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --nologo`
Expected: PASS (el nuevo test + los existentes de `ColumnMapTransformTests` sin regresion — confirma que el cast de Paso 3 sigue igual).

- [ ] **Step 6: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin de Task 1).

- [ ] **Step 7: Commit**

```bash
git add DataPipeline.Connectors/TipoValorConverter.cs DataPipeline.Connectors/Transforms/ColumnMapTransform.cs tests/DataPipeline.Tests/ColumnMapTransformTests.cs
git commit -m "$(cat <<'EOF'
refactor(connectors): extraer TipoValorConverter de ColumnMapTransform

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Connectors — `FileSourceBase` (descubrimiento, orden, watermark, mover a procesados)

**Files:**
- Create: `DataPipeline.Connectors/Sources/FileSourceBase.cs`
- Test: `tests/DataPipeline.Tests/FileSourceBaseTests.cs`

**Interfaces:**
- Consumes: `DataPipeline.Connectors.TipoValorConverter.Convertir` (Task 2); `DataPipeline.Connectors.Sources.RecordBatcher.Batch` (ya existe); `DataPipeline.Core.Records.Record`/`RecordBatch`; `DataPipeline.Core.Runtime.RunContext`; `DataPipeline.Core.Abstractions.ISource`.
- Produces: `public abstract class FileSourceBase : ISource` con:
  - `protected FileSourceBase(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark)`
  - `protected abstract string PatronPorDefecto();`
  - `public abstract IReadOnlyList<string> LeerEncabezado(string rutaArchivo);`
  - `protected abstract IAsyncEnumerable<Record> LeerFilasAsync(string rutaArchivo, IReadOnlyList<string> columnas, CancellationToken ct);`
  - `public string? ArchivoMasNuevoSinProcesar();`
  - `public IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, CancellationToken ct);` (de `ISource`)

  Consumido por `CsvSource` (Task 4), `XlsxSource` (Task 5) y el discoverer de archivo (Task 6).

- [ ] **Step 1: Escribir los tests con un stub minimo (fallan por no existir `FileSourceBase`)**

Crear `tests/DataPipeline.Tests/FileSourceBaseTests.cs`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Tests de la logica comun de origenes de archivo
// (descubrimiento, orden, filtrado/tipado de watermark, mover a procesados), con un stub que no
// depende de CsvHelper/ClosedXML.
using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class FileSourceBaseTests
{
    private sealed class FileSourceDePrueba : FileSourceBase
    {
        private readonly Dictionary<string, (string[] Cols, string[][] Filas)> _contenido;

        public FileSourceDePrueba(string rutaCarpeta, string? patronArchivo, string? columnaWatermark,
            string? tipoColumnaWatermark, Dictionary<string, (string[] Cols, string[][] Filas)> contenido)
            : base(rutaCarpeta, patronArchivo, columnaWatermark, tipoColumnaWatermark) => _contenido = contenido;

        protected override string PatronPorDefecto() => "*.txt";

        public override IReadOnlyList<string> LeerEncabezado(string rutaArchivo) =>
            _contenido[Path.GetFileName(rutaArchivo)].Cols;

        protected override async IAsyncEnumerable<Record> LeerFilasAsync(
            string rutaArchivo, IReadOnlyList<string> columnas, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var fila in _contenido[Path.GetFileName(rutaArchivo)].Filas)
            {
                var valores = new Dictionary<string, object?>(columnas.Count);
                for (int i = 0; i < columnas.Count; i++) valores[columnas[i]] = fila[i];
                yield return new Record { Values = valores };
            }
        }
    }

    private static RunContext Ctx(Dictionary<string, string>? parametros = null) => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static string NuevaCarpeta()
    {
        var d = Path.Combine(Path.GetTempPath(), $"filesrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void CrearArchivo(string carpeta, string nombre, DateTime ultimaEscritura)
    {
        var ruta = Path.Combine(carpeta, nombre);
        File.WriteAllText(ruta, "");
        File.SetLastWriteTimeUtc(ruta, ultimaEscritura);
    }

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Lee_los_archivos_en_orden_de_fecha_y_los_mueve_a_procesados()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "b.txt", new DateTime(2026, 1, 2));
        CrearArchivo(carpeta, "a.txt", new DateTime(2026, 1, 1));
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["a.txt"] = (new[] { "id" }, new[] { new[] { "1" } }),
            ["b.txt"] = (new[] { "id" }, new[] { new[] { "2" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(new[] { "1", "2" }, filas.Select(f => f.Values["id"]));
        Assert.False(File.Exists(Path.Combine(carpeta, "a.txt")));
        Assert.False(File.Exists(Path.Combine(carpeta, "b.txt")));
        Assert.True(File.Exists(Path.Combine(carpeta, "procesados", "a.txt")));
        Assert.True(File.Exists(Path.Combine(carpeta, "procesados", "b.txt")));
    }

    [Fact]
    public async Task No_reprocesa_un_archivo_que_ya_esta_en_procesados()
    {
        var carpeta = NuevaCarpeta();
        Directory.CreateDirectory(Path.Combine(carpeta, "procesados"));
        CrearArchivo(carpeta, "nuevo.txt", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(carpeta, "procesados", "viejo.txt"), "");
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["nuevo.txt"] = (new[] { "id" }, new[] { new[] { "1" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Single(filas);
    }

    [Fact]
    public async Task Filtra_y_tipa_la_columna_watermark_como_fecha()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id", "fecha" }, new[]
            {
                new[] { "1", "2026-01-01" },
                new[] { "2", "2026-01-10" },
                new[] { "3", "2026-01-05" },
            })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", "fecha", "date", contenido);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "2026-01-01T00:00:00.0000000" });

        var filas = await Drenar(source.ReadAsync(ctx, CancellationToken.None));

        // Solo pasan las filas con fecha > 2026-01-01 (id 2 y 3); el valor queda tipado (DateTime, no string).
        Assert.Equal(new[] { "2", "3" }, filas.Select(f => f.Values["id"]));
        Assert.All(filas, f => Assert.IsType<DateTime>(f.Values["fecha"]));
    }

    [Fact]
    public async Task Primera_corrida_sin_watermark_previo_no_filtra_nada()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id", "fecha" }, new[]
            {
                new[] { "1", "2026-01-01" },
                new[] { "2", "2026-01-10" },
            })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", "fecha", "date", contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public async Task Un_archivo_con_el_mismo_nombre_ya_en_procesados_no_se_pisa()
    {
        var carpeta = NuevaCarpeta();
        Directory.CreateDirectory(Path.Combine(carpeta, "procesados"));
        File.WriteAllText(Path.Combine(carpeta, "procesados", "datos.txt"), "viejo");
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id" }, new[] { new[] { "1" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal("viejo", File.ReadAllText(Path.Combine(carpeta, "procesados", "datos.txt")));
        Assert.Single(Directory.GetFiles(carpeta, "*.txt")); // el nuevo no quedo en la carpeta de entrada
        Assert.Equal(2, Directory.GetFiles(Path.Combine(carpeta, "procesados")).Length); // viejo.txt + datos_<sufijo>.txt
    }
}
```

- [ ] **Step 2: Correr los tests — deben fallar (no existe `FileSourceBase`)**

Run: `dotnet test tests/DataPipeline.Tests --filter FileSourceBaseTests --nologo`
Expected: FAIL — error de compilacion.

- [ ] **Step 3: Crear `FileSourceBase.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Logica comun de origenes de archivo: descubrir
// archivos no procesados en la carpeta, ordenarlos por fecha, filtrar/tipar la columna watermark
// (si hay), y mover cada archivo a \procesados solo si se leyo completo sin errores.
using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Connectors.Sources;

/// <summary>
/// Base para origenes que leen archivos de una carpeta (Csv/Xlsx). Un archivo se lee una unica vez
/// en toda su vida: se mueve a \procesados apenas se termina de leer sin error, y nunca se
/// vuelve a mirar. Esto cubre tanto un export "dump acumulativo completo" como uno "delta" sin
/// necesidad de saber cual es: en ambos casos ningun archivo se reprocesa entre corridas.
/// </summary>
public abstract class FileSourceBase : ISource
{
    private readonly string _rutaCarpeta;
    private readonly string? _patronArchivo;
    private readonly string? _columnaWatermark;
    private readonly string? _tipoColumnaWatermark;

    protected FileSourceBase(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark)
    {
        _rutaCarpeta = rutaCarpeta;
        _patronArchivo = patronArchivo;
        _columnaWatermark = string.IsNullOrWhiteSpace(columnaWatermark) ? null : columnaWatermark;
        _tipoColumnaWatermark = tipoColumnaWatermark;
    }

    /// <summary>Patron por defecto cuando no se configuro uno (ej. "*.csv", "*.xlsx").</summary>
    protected abstract string PatronPorDefecto();

    /// <summary>Nombres de columna del archivo (primera fila si tiene encabezado; Col1..N si no).</summary>
    public abstract IReadOnlyList<string> LeerEncabezado(string rutaArchivo);

    /// <summary>Filas de datos del archivo, en el mismo orden que <paramref name="columnas"/>.</summary>
    protected abstract IAsyncEnumerable<Record> LeerFilasAsync(
        string rutaArchivo, IReadOnlyList<string> columnas, CancellationToken ct);

    /// <summary>El archivo sin procesar mas reciente, o null si no hay ninguno. Usado para "traer columnas".</summary>
    public string? ArchivoMasNuevoSinProcesar() => DescubrirArchivos().LastOrDefault();

    private List<string> DescubrirArchivos()
    {
        if (!Directory.Exists(_rutaCarpeta)) return new();
        var patron = string.IsNullOrWhiteSpace(_patronArchivo) ? PatronPorDefecto() : _patronArchivo;
        // Directory.GetFiles sin SearchOption.AllDirectories no baja a \procesados: excluido gratis.
        return Directory.GetFiles(_rutaCarpeta, patron)
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        object? watermarkTipado = null;
        if (_columnaWatermark is not null && ctx.Parameters.TryGetValue("watermark", out var crudoWatermark))
            watermarkTipado = TipoValorConverter.Convertir(crudoWatermark, _tipoColumnaWatermark ?? "string");

        foreach (var archivo in DescubrirArchivos())
        {
            var columnas = LeerEncabezado(archivo);
            var claveWatermark = _columnaWatermark is null
                ? null
                : columnas.FirstOrDefault(c => string.Equals(c, _columnaWatermark, StringComparison.OrdinalIgnoreCase));

            await foreach (var lote in RecordBatcher.Batch(
                columnas, FiltrarYTipar(archivo, columnas, claveWatermark, watermarkTipado, ct), ctx.BatchSize, ct))
                yield return lote;

            MoverAProcesados(archivo);
        }
    }

    private async IAsyncEnumerable<Record> FiltrarYTipar(
        string archivo, IReadOnlyList<string> columnas, string? claveWatermark, object? watermarkTipado,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var fila in LeerFilasAsync(archivo, columnas, ct).WithCancellation(ct))
        {
            if (claveWatermark is null) { yield return fila; continue; }

            var valores = new Dictionary<string, object?>(fila.Values);
            if (valores.TryGetValue(claveWatermark, out var crudo) && crudo is not null)
            {
                var tipado = TipoValorConverter.Convertir(crudo, _tipoColumnaWatermark ?? "string");
                valores[claveWatermark] = tipado;
                if (watermarkTipado is not null && Comparer<object>.Default.Compare(tipado, watermarkTipado) <= 0)
                    continue; // ya visto en una corrida anterior
            }
            yield return new Record { Values = valores };
        }
    }

    private static void MoverAProcesados(string rutaArchivo)
    {
        var carpetaProcesados = Path.Combine(Path.GetDirectoryName(rutaArchivo)!, "procesados");
        Directory.CreateDirectory(carpetaProcesados);
        var nombre = Path.GetFileName(rutaArchivo);
        var destino = Path.Combine(carpetaProcesados, nombre);
        if (File.Exists(destino))
        {
            var sufijo = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            destino = Path.Combine(carpetaProcesados,
                $"{Path.GetFileNameWithoutExtension(nombre)}_{sufijo}{Path.GetExtension(nombre)}");
        }
        File.Move(rutaArchivo, destino);
    }
}
```

- [ ] **Step 4: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --filter FileSourceBaseTests --nologo`
Expected: PASS (los 5 tests).

- [ ] **Step 5: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Connectors/Sources/FileSourceBase.cs tests/DataPipeline.Tests/FileSourceBaseTests.cs
git commit -m "$(cat <<'EOF'
feat(connectors): FileSourceBase — descubrimiento, orden, watermark tipado, mover a procesados

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Connectors — `CsvSource` (CsvHelper) + `SourceFactory`

**Files:**
- Modify: `DataPipeline.Connectors/DataPipeline.Connectors.csproj`
- Create: `DataPipeline.Connectors/Sources/CsvSource.cs`
- Modify: `DataPipeline.Connectors/Factories/SourceFactory.cs`
- Test: `tests/DataPipeline.Tests/CsvSourceTests.cs`
- Test: `tests/DataPipeline.Tests/SourceFactoryTests.cs`

**Interfaces:**
- Consumes: `FileSourceBase` (Task 3).
- Produces: `public sealed class CsvSource : FileSourceBase` con ctor `(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark, string? separador, string? encoding, bool tieneEncabezado)`. `SourceFactory` resuelve `"CsvSource"`. Consumido por Task 6 (discoverer) y Task 7 (hydrator).

- [ ] **Step 1: Agregar el paquete NuGet**

En `DataPipeline.Connectors/DataPipeline.Connectors.csproj`, agregar dentro del `<ItemGroup>` de `PackageReference` (comentario arriba del grupo, ya que el archivo entero se edita):

```xml
  <!-- Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - CsvHelper (CsvSource) + ClosedXML (XlsxSource, Task 5). -->
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.1" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.8" />
    <PackageReference Include="NCalc" Version="6.2.0" />
    <PackageReference Include="CsvHelper" Version="33.0.1" />
    <PackageReference Include="ClosedXML" Version="0.104.2" />
  </ItemGroup>
```

Run: `dotnet restore DataPipeline.Connectors`
Expected: restaura sin error (ajustar la version exacta si NuGet ofrece una mas nueva al restaurar; usar la que quede resuelta).

- [ ] **Step 2: Escribir los tests con archivos CSV reales (fallan por no existir `CsvSource`)**

Crear `tests/DataPipeline.Tests/CsvSourceTests.cs`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Tests de CsvSource con archivos reales en disco:
// separador/encoding no default, sin encabezado, y watermark end-to-end.
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;

namespace DataPipeline.Tests;

public class CsvSourceTests
{
    private static RunContext Ctx(Dictionary<string, string>? parametros = null) => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static string NuevaCarpeta()
    {
        var d = Path.Combine(Path.GetTempPath(), $"csvsrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Lee_un_csv_con_punto_y_coma_y_encabezado()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id;importe\n1;100\n2;200\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ";", "UTF-8", true);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.Equal("100", filas[0].Values["importe"]);
    }

    [Fact]
    public async Task Sin_encabezado_nombra_las_columnas_Col1_ColN()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "datos.csv"), "1,100\n2,200\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ",", "UTF-8", false);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.Equal("100", filas[0].Values["Col2"]);
    }

    [Fact]
    public async Task Respeta_comillas_con_el_separador_adentro()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id,nombre\n1,\"Perez, Juan\"\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ",", "UTF-8", true);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal("Perez, Juan", filas[0].Values["nombre"]);
    }

    [Fact]
    public async Task Filtra_por_watermark_tipado_como_int_a_traves_de_dos_archivos()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "a_20260901.csv"), "id\n1\n2\n");
        File.SetLastWriteTimeUtc(Path.Combine(carpeta, "a_20260901.csv"), new DateTime(2026, 9, 1));
        File.WriteAllText(Path.Combine(carpeta, "b_20260902.csv"), "id\n2\n3\n");
        File.SetLastWriteTimeUtc(Path.Combine(carpeta, "b_20260902.csv"), new DateTime(2026, 9, 2));
        var source = new CsvSource(carpeta, "*.csv", "id", "int", ",", "UTF-8", true);

        // Primera corrida: sin watermark previo, trae todo (incluye repetidos entre archivos: caso "dump").
        var primera = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));
        Assert.Equal(4, primera.Count);

        // Ambos archivos ya se movieron a procesados: una segunda corrida sobre la misma carpeta no trae nada mas.
        var segunda = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));
        Assert.Empty(segunda);
    }
}
```

- [ ] **Step 3: Correr los tests — deben fallar (no existe `CsvSource`)**

Run: `dotnet test tests/DataPipeline.Tests --filter CsvSourceTests --nologo`
Expected: FAIL — error de compilacion.

- [ ] **Step 4: Crear `CsvSource.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origen CSV: separador/encoding/encabezado
// configurables por DataSource. Usa CsvHelper (no Split a mano) porque un ERP viejo puede exportar
// campos entre comillas con el separador o saltos de linea adentro.
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Sources;

public sealed class CsvSource : FileSourceBase
{
    private readonly string _separador;
    private readonly string _encoding;
    private readonly bool _tieneEncabezado;

    public CsvSource(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark,
        string? separador, string? encoding, bool tieneEncabezado)
        : base(rutaCarpeta, patronArchivo, columnaWatermark, tipoColumnaWatermark)
    {
        _separador = string.IsNullOrWhiteSpace(separador) ? "," : separador;
        _encoding = string.IsNullOrWhiteSpace(encoding) ? "UTF-8" : encoding;
        _tieneEncabezado = tieneEncabezado;
    }

    protected override string PatronPorDefecto() => "*.csv";

    private CsvConfiguration Configuracion() => new(CultureInfo.InvariantCulture)
    {
        Delimiter = _separador,
        HasHeaderRecord = _tieneEncabezado,
        MissingFieldFound = null,
        BadDataFound = null
    };

    // .NET no incluye Windows-1252; Latin1 (ISO-8859-1, incluido desde .NET 5) es equivalente para
    // el rango de caracteres tipico de un export de ERP (difiere solo en simbolos raros 0x80-0x9F).
    private Encoding ResolverEncoding() =>
        string.Equals(_encoding, "Windows-1252", StringComparison.OrdinalIgnoreCase) ? Encoding.Latin1 : Encoding.UTF8;

    public override IReadOnlyList<string> LeerEncabezado(string rutaArchivo)
    {
        using var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lector = new StreamReader(stream, ResolverEncoding());
        using var csv = new CsvReader(lector, Configuracion());

        if (!_tieneEncabezado)
        {
            if (!csv.Read()) return Array.Empty<string>();
            return Enumerable.Range(1, csv.Parser.Count).Select(i => $"Col{i}").ToArray();
        }

        csv.Read();
        csv.ReadHeader();
        return csv.HeaderRecord ?? Array.Empty<string>();
    }

    protected override async IAsyncEnumerable<Record> LeerFilasAsync(
        string rutaArchivo, IReadOnlyList<string> columnas, [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lector = new StreamReader(stream, ResolverEncoding());
        using var csv = new CsvReader(lector, Configuracion());

        if (_tieneEncabezado)
        {
            await csv.ReadAsync();
            csv.ReadHeader();
        }

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var valores = new Dictionary<string, object?>(columnas.Count);
            for (int i = 0; i < columnas.Count; i++)
                valores[columnas[i]] = csv.GetField(i);
            yield return new Record { Values = valores };
        }
    }
}
```

- [ ] **Step 5: Agregar el caso `"CsvSource"` a `SourceFactory.cs`**

En `DataPipeline.Connectors/Factories/SourceFactory.cs`, agregar el comentario y el caso dentro del `switch`, y los dos helpers de lectura opcional debajo de `LeerString`:

```csharp
        return step.Conector switch
        {
            "SqlServerSource" => new SqlServerSource(
                LeerString(step, "connectionString"),
                LeerString(step, "sql"),
                step.Configuracion.TryGetValue("connectionStringEncrypted", out var e)
                    && e.ValueKind == JsonValueKind.True),
            // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx).
            "CsvSource" => new CsvSource(
                LeerString(step, "rutaCarpeta"),
                LeerStringOpcional(step, "patronArchivo"),
                LeerStringOpcional(step, "columnaWatermark"),
                LeerStringOpcional(step, "tipoColumnaWatermark"),
                LeerStringOpcional(step, "separador"),
                LeerStringOpcional(step, "encoding"),
                LeerBoolOpcional(step, "tieneEncabezado") ?? true),
            _ => throw new NotSupportedException($"Conector de origen no soportado: '{step.Conector}'.")
        };
    }

    private static string LeerString(StepDefinition step, string clave)
    {
        if (step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        throw new ArgumentException($"El origen '{step.Nombre}' requiere la config string '{clave}'.", nameof(step));
    }

    private static string? LeerStringOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? LeerBoolOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) &&
        (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : (bool?)null;
```

(Reemplaza desde el `return step.Conector switch` hasta el cierre del metodo `LeerString` existente; el `using DataPipeline.Connectors.Sources;` de arriba del archivo ya cubre `CsvSource`.)

- [ ] **Step 6: Agregar el test de fabrica en `SourceFactoryTests.cs`**

Agregar (dentro de la clase, junto a los demas `[Fact]`):

```csharp
    [Fact]
    public void Crea_un_CsvSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "CsvSource",
            Configuracion = new() { ["rutaCarpeta"] = S(@"C:\exports") }
        };

        var source = factory.Create(step);

        Assert.IsType<CsvSource>(source);
    }
```

- [ ] **Step 7: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --filter "CsvSourceTests|SourceFactoryTests" --nologo`
Expected: PASS (todos).

- [ ] **Step 8: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin).

- [ ] **Step 9: Commit**

```bash
git add DataPipeline.Connectors/DataPipeline.Connectors.csproj DataPipeline.Connectors/Sources/CsvSource.cs DataPipeline.Connectors/Factories/SourceFactory.cs tests/DataPipeline.Tests/CsvSourceTests.cs tests/DataPipeline.Tests/SourceFactoryTests.cs
git commit -m "$(cat <<'EOF'
feat(connectors): CsvSource (CsvHelper) + SourceFactory

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Connectors — `XlsxSource` (ClosedXML) + `SourceFactory`

**Files:**
- Create: `DataPipeline.Connectors/Sources/XlsxSource.cs`
- Modify: `DataPipeline.Connectors/Factories/SourceFactory.cs`
- Test: `tests/DataPipeline.Tests/XlsxSourceTests.cs`
- Test: `tests/DataPipeline.Tests/SourceFactoryTests.cs`

**Interfaces:**
- Consumes: `FileSourceBase` (Task 3). `ClosedXML` ya referenciado (Task 4, mismo `.csproj`).
- Produces: `public sealed class XlsxSource : FileSourceBase` con ctor `(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark, string? hojaExcel)`. `SourceFactory` resuelve `"XlsxSource"`. Consumido por Task 6 y Task 7.

- [ ] **Step 1: Escribir los tests con archivos XLSX reales (fallan por no existir `XlsxSource`)**

Crear `tests/DataPipeline.Tests/XlsxSourceTests.cs`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Tests de XlsxSource con libros reales generados por
// ClosedXML (mismo patron de lectura que ExcelQueryExecutor de ConsultasGenericas: primera fila =
// encabezado), agregando hoja nombrada y watermark.
using ClosedXML.Excel;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;

namespace DataPipeline.Tests;

public class XlsxSourceTests
{
    private static RunContext Ctx(Dictionary<string, string>? parametros = null) => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static string NuevaCarpeta()
    {
        var d = Path.Combine(Path.GetTempPath(), $"xlsxsrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void CrearLibro(string ruta, string nombreHoja, string[] encabezado, string[][] filas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add(nombreHoja);
        for (int c = 0; c < encabezado.Length; c++) hoja.Cell(1, c + 1).Value = encabezado[c];
        for (int f = 0; f < filas.Length; f++)
            for (int c = 0; c < filas[f].Length; c++)
                hoja.Cell(f + 2, c + 1).Value = filas[f][c];
        libro.SaveAs(ruta);
    }

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Lee_la_primera_hoja_por_defecto()
    {
        var carpeta = NuevaCarpeta();
        CrearLibro(Path.Combine(carpeta, "ventas.xlsx"), "Datos",
            new[] { "id", "importe" }, new[] { new[] { "1", "100" }, new[] { "2", "200" } });
        var source = new XlsxSource(carpeta, "*.xlsx", null, null, null);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.Equal("100", filas[0].Values["importe"]);
    }

    [Fact]
    public async Task Lee_la_hoja_indicada_por_nombre()
    {
        var carpeta = NuevaCarpeta();
        var ruta = Path.Combine(carpeta, "libro.xlsx");
        using (var libro = new XLWorkbook())
        {
            libro.Worksheets.Add("Resumen");
            var hoja = libro.Worksheets.Add("Detalle");
            hoja.Cell(1, 1).Value = "id";
            hoja.Cell(2, 1).Value = "9";
            libro.SaveAs(ruta);
        }
        var source = new XlsxSource(carpeta, "*.xlsx", null, null, "Detalle");

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Single(filas);
        Assert.Equal("9", filas[0].Values["id"]);
    }

    [Fact]
    public async Task Hoja_inexistente_lanza_con_mensaje_claro()
    {
        var carpeta = NuevaCarpeta();
        CrearLibro(Path.Combine(carpeta, "ventas.xlsx"), "Datos", new[] { "id" }, new[] { new[] { "1" } });
        var source = new XlsxSource(carpeta, "*.xlsx", null, null, "NoExiste");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Drenar(source.ReadAsync(Ctx(), CancellationToken.None)));
    }

    [Fact]
    public async Task Filtra_por_watermark_tipado_como_decimal()
    {
        var carpeta = NuevaCarpeta();
        CrearLibro(Path.Combine(carpeta, "ventas.xlsx"), "Datos",
            new[] { "id", "monto" }, new[] { new[] { "1", "10.5" }, new[] { "2", "20.5" } });
        var source = new XlsxSource(carpeta, "*.xlsx", "monto", "decimal", null);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "10.5" });

        var filas = await Drenar(source.ReadAsync(ctx, CancellationToken.None));

        Assert.Single(filas);
        Assert.Equal("2", filas[0].Values["id"]);
    }
}
```

- [ ] **Step 2: Correr los tests — deben fallar (no existe `XlsxSource`)**

Run: `dotnet test tests/DataPipeline.Tests --filter XlsxSourceTests --nologo`
Expected: FAIL — error de compilacion.

- [ ] **Step 3: Crear `XlsxSource.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origen Xlsx: misma libreria y patron de lectura
// (ClosedXML, primera fila = encabezado) que ExcelQueryExecutor de ConsultasGenericas, con hoja
// configurable y sin depender de un archivo fijo (ver FileSourceBase para carpeta/orden/watermark).
using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Sources;

public sealed class XlsxSource : FileSourceBase
{
    private readonly string? _hojaExcel;

    public XlsxSource(string rutaCarpeta, string? patronArchivo, string? columnaWatermark, string? tipoColumnaWatermark,
        string? hojaExcel)
        : base(rutaCarpeta, patronArchivo, columnaWatermark, tipoColumnaWatermark) => _hojaExcel = hojaExcel;

    protected override string PatronPorDefecto() => "*.xlsx";

    private IXLWorksheet ResolverHoja(XLWorkbook libro)
    {
        if (string.IsNullOrWhiteSpace(_hojaExcel)) return libro.Worksheets.First();
        if (!libro.TryGetWorksheet(_hojaExcel, out var hoja))
            throw new InvalidOperationException($"No se encontro la hoja '{_hojaExcel}' en el archivo.");
        return hoja;
    }

    public override IReadOnlyList<string> LeerEncabezado(string rutaArchivo)
    {
        using var libro = new XLWorkbook(rutaArchivo);
        var hoja = ResolverHoja(libro);
        var rango = hoja.RangeUsed();
        if (rango is null) return Array.Empty<string>();
        var primeraFila = rango.RowsUsed().First();
        return primeraFila.Cells().Select(c => c.GetValue<string>()).ToArray();
    }

    protected override async IAsyncEnumerable<Record> LeerFilasAsync(
        string rutaArchivo, IReadOnlyList<string> columnas, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield(); // ClosedXML es sincronico; el await mantiene el metodo como iterator async real.
        using var libro = new XLWorkbook(rutaArchivo);
        var hoja = ResolverHoja(libro);
        var rango = hoja.RangeUsed();
        if (rango is null) yield break;

        foreach (var fila in rango.RowsUsed().Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            var celdas = fila.Cells().ToList();
            var valores = new Dictionary<string, object?>(columnas.Count);
            for (int i = 0; i < columnas.Count; i++)
                valores[columnas[i]] = i < celdas.Count ? celdas[i].Value.ToString() : null;
            yield return new Record { Values = valores };
        }
    }
}
```

- [ ] **Step 4: Agregar el caso `"XlsxSource"` a `SourceFactory.cs`**

Agregar el caso al `switch` (junto al de `"CsvSource"` agregado en Task 4):

```csharp
            "XlsxSource" => new XlsxSource(
                LeerString(step, "rutaCarpeta"),
                LeerStringOpcional(step, "patronArchivo"),
                LeerStringOpcional(step, "columnaWatermark"),
                LeerStringOpcional(step, "tipoColumnaWatermark"),
                LeerStringOpcional(step, "hojaExcel")),
```

- [ ] **Step 5: Agregar el test de fabrica en `SourceFactoryTests.cs`**

```csharp
    [Fact]
    public void Crea_un_XlsxSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "XlsxSource",
            Configuracion = new() { ["rutaCarpeta"] = S(@"C:\exports") }
        };

        var source = factory.Create(step);

        Assert.IsType<XlsxSource>(source);
    }
```

- [ ] **Step 6: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --filter "XlsxSourceTests|SourceFactoryTests" --nologo`
Expected: PASS (todos).

- [ ] **Step 7: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin).

- [ ] **Step 8: Commit**

```bash
git add DataPipeline.Connectors/Sources/XlsxSource.cs DataPipeline.Connectors/Factories/SourceFactory.cs tests/DataPipeline.Tests/XlsxSourceTests.cs tests/DataPipeline.Tests/SourceFactoryTests.cs
git commit -m "$(cat <<'EOF'
feat(connectors): XlsxSource (ClosedXML) + SourceFactory

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Api — `IConnectionTester.ProbarArchivoAsync` + `ISchemaDiscoverer.DescubrirArchivoAsync`

**Files:**
- Modify: `DataPipeline.Core/Abstractions/IConnectionTester.cs`
- Modify: `DataPipeline.Connectors/Testing/ConnectionTester.cs`
- Modify: `DataPipeline.Core/Abstractions/ISchemaDiscoverer.cs`
- Modify: `DataPipeline.Connectors/Testing/SqlServerSchemaDiscoverer.cs`
- Modify: `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs` (los fakes locales dejan de compilar si no se actualizan)
- Test: `tests/DataPipeline.Tests/ConnectionTesterArchivoTests.cs`
- Test: `tests/DataPipeline.Tests/SchemaDiscovererArchivoTests.cs`

**Interfaces:**
- Consumes: `FileSourceBase`/`CsvSource`/`XlsxSource` (Tasks 3-5).
- Produces: `IConnectionTester.ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct)`; `ISchemaDiscoverer.DescubrirArchivoAsync(DataSource ds, CancellationToken ct)`. Consumido por `DataSourcesController` (Task 8).

- [ ] **Step 1: Escribir los tests (fallan por no existir los metodos)**

Crear `tests/DataPipeline.Tests/ConnectionTesterArchivoTests.cs`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1
using DataPipeline.Connectors.Testing;
using Xunit;

namespace DataPipeline.Tests;

public class ConnectionTesterArchivoTests
{
    [Fact]
    public async Task Carpeta_inexistente_da_Ok_false()
    {
        var tester = new ConnectionTester();
        var r = await tester.ProbarArchivoAsync(@"C:\no-existe-" + Guid.NewGuid(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public async Task Carpeta_existente_y_escribible_da_Ok_true()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"probar_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        var tester = new ConnectionTester();

        var r = await tester.ProbarArchivoAsync(carpeta, CancellationToken.None);

        Assert.True(r.Ok);
    }
}
```

Crear `tests/DataPipeline.Tests/SchemaDiscovererArchivoTests.cs`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1
using DataPipeline.Connectors.Testing;
using DataPipeline.Core.DataSources;
using Xunit;

namespace DataPipeline.Tests;

public class SchemaDiscovererArchivoTests
{
    [Fact]
    public async Task Descubre_columnas_del_csv_mas_nuevo_sin_procesar()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"disc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id,importe\n1,100\n");
        var ds = new DataSource { Tipo = TipoDataSource.Csv, RutaCarpeta = carpeta, Separador = ",", TieneEncabezado = true };
        var discoverer = new SqlServerSchemaDiscoverer();

        var r = await discoverer.DescubrirArchivoAsync(ds, CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(new[] { "id", "importe" }, r.Columnas!.Select(c => c.Nombre));
    }

    [Fact]
    public async Task Sin_archivos_da_Ok_false_con_mensaje()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"disc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        var ds = new DataSource { Tipo = TipoDataSource.Csv, RutaCarpeta = carpeta };
        var discoverer = new SqlServerSchemaDiscoverer();

        var r = await discoverer.DescubrirArchivoAsync(ds, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }
}
```

- [ ] **Step 2: Correr los tests — deben fallar (no compila)**

Run: `dotnet test tests/DataPipeline.Tests --filter "ConnectionTesterArchivoTests|SchemaDiscovererArchivoTests" --nologo`
Expected: FAIL — error de compilacion (los metodos no existen).

- [ ] **Step 3: Agregar el metodo a `IConnectionTester.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Prueba de origenes de archivo (Csv/Xlsx).
namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de probar una conexion a un origen.</summary>
public sealed record ConnectionResult(bool Ok, string? Error);

/// <summary>Prueba de conectividad contra un origen (no lee datos).</summary>
public interface IConnectionTester
{
    Task<ConnectionResult> ProbarSqlServerAsync(string connectionString, CancellationToken ct);

    /// <summary>Valida que la carpeta exista y sea escribible (necesario para crear \procesados).</summary>
    Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct);
}
```

- [ ] **Step 4: Implementar en `ConnectionTester.cs`**

Agregar el metodo a la clase (despues de `ProbarSqlServerAsync`):

```csharp
    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo: valida que la carpeta
    // exista y sea escribible (ahi se crea \procesados al correr el pipeline).
    public async Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rutaCarpeta))
                return new ConnectionResult(false, "La ruta de la carpeta esta vacia.");
            if (!Directory.Exists(rutaCarpeta))
                return new ConnectionResult(false, $"No se encontro la carpeta: {rutaCarpeta}");

            var carpetaProcesados = Path.Combine(rutaCarpeta, "procesados");
            Directory.CreateDirectory(carpetaProcesados);
            var archivoPrueba = Path.Combine(carpetaProcesados, $".probar_{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(archivoPrueba, "", ct);
            File.Delete(archivoPrueba);

            return new ConnectionResult(true, null);
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ex.Message);
        }
    }
```

- [ ] **Step 5: Agregar el metodo a `ISchemaDiscoverer.cs`**

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Descubrimiento de columnas para origenes de archivo.
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resultado de descubrir el esquema del resultado de un SQL (sin traer datos).</summary>
public sealed record SchemaResult(bool Ok, IReadOnlyList<ColumnaOrigen>? Columnas, string? Error);

/// <summary>Descubre las columnas de un origen sin traer datos.</summary>
public interface ISchemaDiscoverer
{
    Task<SchemaResult> DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct);

    /// <summary>Csv/Xlsx: lee el encabezado del archivo sin procesar mas nuevo de <paramref name="ds"/>.</summary>
    Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct);
}
```

- [ ] **Step 6: Implementar en `SqlServerSchemaDiscoverer.cs`**

Agregar el `using` de `DataPipeline.Connectors.Sources` arriba, y el metodo despues de `DescubrirSqlServerAsync`:

```csharp
    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo: abre el archivo mas nuevo
    // sin procesar y lee su encabezado (no hace falta nada del cliente, a diferencia del SQL que el
    // usuario esta escribiendo en el editor).
    public Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct)
    {
        try
        {
            FileSourceBase fuente = ds.Tipo switch
            {
                TipoDataSource.Csv => new CsvSource(ds.RutaCarpeta ?? "", ds.PatronArchivo, null, null,
                    ds.Separador, ds.Encoding, ds.TieneEncabezado),
                TipoDataSource.Xlsx => new XlsxSource(ds.RutaCarpeta ?? "", ds.PatronArchivo, null, null, ds.HojaExcel),
                _ => throw new NotSupportedException($"Tipo no soportado para descubrir archivo: '{ds.Tipo}'.")
            };

            var archivo = fuente.ArchivoMasNuevoSinProcesar();
            if (archivo is null)
                return Task.FromResult(new SchemaResult(false, null,
                    "No hay archivos sin procesar en la carpeta que matcheen el patron."));

            var columnas = fuente.LeerEncabezado(archivo)
                .Select(n => new ColumnaOrigen { Nombre = n, Tipo = "string" })
                .ToList();
            return Task.FromResult(new SchemaResult(true, columnas, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SchemaResult(false, null, ex.Message));
        }
    }
```

- [ ] **Step 7: Actualizar los fakes de `DataSourcesColumnasTests.cs`**

En `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs`, agregar el metodo faltante a cada fake (si no, no compila):

```csharp
    private sealed class FakeTester : IConnectionTester
    {
        public Task<ConnectionResult> ProbarSqlServerAsync(string cs, CancellationToken ct) =>
            Task.FromResult(new ConnectionResult(true, null));

        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - implementacion minima para no romper el fake.
        public Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct) =>
            Task.FromResult(new ConnectionResult(true, null));
    }

    private sealed class FakeDiscoverer : ISchemaDiscoverer
    {
        private readonly SchemaResult _r;
        public FakeDiscoverer(SchemaResult r) => _r = r;
        public Task<SchemaResult> DescubrirSqlServerAsync(string cs, string sql, CancellationToken ct) =>
            Task.FromResult(_r);

        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - implementacion minima para no romper el fake.
        public Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct) =>
            Task.FromResult(_r);
    }
```

- [ ] **Step 8: Correr todos los tests — deben pasar**

Run: `dotnet test --nologo`
Expected: PASS (toda la suite; confirma que los fakes actualizados no rompieron `DataSourcesColumnasTests`).

- [ ] **Step 9: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin de Task 1 — este task no lo arregla).

- [ ] **Step 10: Commit**

```bash
git add DataPipeline.Core/Abstractions/IConnectionTester.cs DataPipeline.Connectors/Testing/ConnectionTester.cs DataPipeline.Core/Abstractions/ISchemaDiscoverer.cs DataPipeline.Connectors/Testing/SqlServerSchemaDiscoverer.cs tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs tests/DataPipeline.Tests/ConnectionTesterArchivoTests.cs tests/DataPipeline.Tests/SchemaDiscovererArchivoTests.cs
git commit -m "$(cat <<'EOF'
feat(api): IConnectionTester.ProbarArchivoAsync + ISchemaDiscoverer.DescubrirArchivoAsync

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Engine — `PipelineHydrator` rama Csv/Xlsx

**Files:**
- Modify: `DataPipeline.Engine/PipelineHydrator.cs`
- Test: `tests/DataPipeline.Tests/PipelineHydratorTests.cs`

**Interfaces:**
- Consumes: `DataSource.RutaCarpeta`/etc. (Task 1).
- Produces: `PipelineHydrator.HydrateAsync` inyecta `rutaCarpeta`/`patronArchivo`/`separador`/`encoding`/`tieneEncabezado`/`hojaExcel` en `Configuracion` para pasos Csv/Xlsx. Consumido por `RunsController`/`EjecutorProgramado` (sin cambios, ya llaman a `HydrateAsync`).

- [ ] **Step 1: Escribir el test (falla — hoy solo inyecta `connectionString` para cualquier tipo)**

Agregar a `tests/DataPipeline.Tests/PipelineHydratorTests.cs` (junto a los `[Fact]` existentes):

```csharp
    [Fact]
    public async Task Inyecta_config_de_carpeta_para_un_DataSource_Csv()
    {
        var id = Guid.NewGuid();
        var ds = new DataSource
        {
            Id = id, Nombre = "ventas-csv", Tipo = TipoDataSource.Csv,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.csv",
            Separador = ";", Encoding = "Windows-1252", TieneEncabezado = true
        };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), Enc());

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "CsvSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()) } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.Equal(@"\\erp\exports", cfg["rutaCarpeta"].GetString());
        Assert.Equal("VENTAS_*.csv", cfg["patronArchivo"].GetString());
        Assert.Equal(";", cfg["separador"].GetString());
        Assert.Equal("Windows-1252", cfg["encoding"].GetString());
        Assert.True(cfg["tieneEncabezado"].GetBoolean());
        Assert.False(cfg.ContainsKey("connectionString"));
    }

    [Fact]
    public async Task Inyecta_config_de_hoja_para_un_DataSource_Xlsx()
    {
        var id = Guid.NewGuid();
        var ds = new DataSource
        {
            Id = id, Nombre = "ventas-xlsx", Tipo = TipoDataSource.Xlsx,
            RutaCarpeta = @"\\erp\exports", HojaExcel = "Datos"
        };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), Enc());

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "XlsxSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()) } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.Equal("Datos", cfg["hojaExcel"].GetString());
    }
```

- [ ] **Step 2: Correr los tests — deben fallar**

Run: `dotnet test tests/DataPipeline.Tests --filter "PipelineHydratorTests" --nologo`
Expected: FAIL — los 2 tests nuevos, `KeyNotFoundException` al leer `cfg["rutaCarpeta"]` (hoy solo se inyecta `connectionString`).

- [ ] **Step 3: Agregar la rama Csv/Xlsx a `PipelineHydrator.cs`**

Reemplazar el cuerpo del `foreach` en `HydrateAsync`:

```csharp
            var ds = await _origenes.GetByIdAsync(id, ct);
            if (ds is null) continue;

            // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo: la config del paso
            // sale del DataSource (no hay secreto que desencriptar, a diferencia de connectionString).
            if (ds.Tipo == TipoDataSource.SqlServer)
            {
                var cs = string.IsNullOrEmpty(ds.ConnectionString) ? "" : _encryption.Decrypt(ds.ConnectionString);
                paso.Configuracion["connectionString"] = JsonSerializer.SerializeToElement(cs);
                continue;
            }

            paso.Configuracion["rutaCarpeta"] = JsonSerializer.SerializeToElement(ds.RutaCarpeta ?? "");
            paso.Configuracion["patronArchivo"] = JsonSerializer.SerializeToElement(ds.PatronArchivo ?? "");
            if (ds.Tipo == TipoDataSource.Csv)
            {
                paso.Configuracion["separador"] = JsonSerializer.SerializeToElement(ds.Separador ?? ",");
                paso.Configuracion["encoding"] = JsonSerializer.SerializeToElement(ds.Encoding ?? "UTF-8");
                paso.Configuracion["tieneEncabezado"] = JsonSerializer.SerializeToElement(ds.TieneEncabezado);
            }
            else if (ds.Tipo == TipoDataSource.Xlsx)
            {
                paso.Configuracion["hojaExcel"] = JsonSerializer.SerializeToElement(ds.HojaExcel ?? "");
            }
```

(Va en lugar de las 2 lineas actuales que arman `cs` e inyectan `connectionString` sin condicion.)

- [ ] **Step 4: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Tests --filter "PipelineHydratorTests" --nologo`
Expected: PASS (los 4: 2 existentes + 2 nuevos).

- [ ] **Step 5: Build limpio**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s) (salvo el break ya anotado en Api/Admin).

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Engine/PipelineHydrator.cs tests/DataPipeline.Tests/PipelineHydratorTests.cs
git commit -m "$(cat <<'EOF'
feat(engine): PipelineHydrator inyecta config de carpeta para origenes Csv/Xlsx

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Api — `DataSourcesController` (campos nuevos + Probar/Columnas ramificados)

**Files:**
- Modify: `DataPipeline.Api/Controllers/DataSourcesController.cs`
- Test: `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs`

**Interfaces:**
- Consumes: `DataSource` (Task 1), `IConnectionTester.ProbarArchivoAsync`/`ISchemaDiscoverer.DescubrirArchivoAsync` (Task 6).
- Produces: `DataSourceUpsert` con los 6 campos nuevos (sin `FilePath`); `DataSourceVista` con los 6 campos nuevos; `ColumnasReq.Sql` opcional. Consumido por `DataPipeline.Admin` (Task 9/10).

**Nota:** este task arregla el break de build de `DataPipeline.Api` dejado por Task 1 (uso de `DataSource.FilePath`, eliminado).

- [ ] **Step 1: Escribir los tests de la rama de archivo (fallan por comportamiento nuevo)**

Agregar a `tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs` (los fakes ya se actualizaron en Task 6):

Primero, reemplazar `FakeTester` completo (agrega flags para distinguir que rama se llamo — sin esto, "usa ProbarArchivoAsync" no seria un assert significativo):

```csharp
    private sealed class FakeTester : IConnectionTester
    {
        public bool SqlLlamado, ArchivoLlamado;
        public Task<ConnectionResult> ProbarSqlServerAsync(string cs, CancellationToken ct)
        {
            SqlLlamado = true;
            return Task.FromResult(new ConnectionResult(true, null));
        }
        public Task<ConnectionResult> ProbarArchivoAsync(string rutaCarpeta, CancellationToken ct)
        {
            ArchivoLlamado = true;
            return Task.FromResult(new ConnectionResult(true, null));
        }
    }
```

Agregar un segundo helper que expone el `FakeTester` usado (el helper `Controller(...)` existente queda igual, para los tests que no lo necesitan):

```csharp
    private static (DataSourcesController Ctrl, FakeTester Tester) ControllerConTester(DataSource? ds, SchemaResult r)
    {
        var enc = Enc();
        if (ds is not null && !string.IsNullOrEmpty(ds.ConnectionString))
            ds.ConnectionString = enc.Encrypt(ds.ConnectionString);
        var tester = new FakeTester();
        return (new DataSourcesController(new FakeDsStore(ds), enc, tester, new FakeDiscoverer(r)), tester);
    }
```

Y agregar los dos tests nuevos:

```csharp
    [Fact]
    public async Task Columnas_de_un_DataSource_Csv_no_requiere_Sql()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.Csv, RutaCarpeta = @"C:\x" };
        var cols = new List<ColumnaOrigen> { new() { Nombre = "id", Tipo = "string" } };
        var ctrl = Controller(ds, new SchemaResult(true, cols, null));

        var res = await ctrl.Columnas(ds.Id, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var lista = Assert.IsAssignableFrom<IReadOnlyList<ColumnaOrigen>>(ok.Value);
        Assert.Single(lista);
    }

    [Fact]
    public async Task Probar_un_DataSource_Csv_usa_ProbarArchivoAsync()
    {
        var ds = new DataSource { Id = Guid.NewGuid(), Nombre = "d", Tipo = TipoDataSource.Csv, RutaCarpeta = @"C:\x" };
        var (ctrl, tester) = ControllerConTester(ds, new SchemaResult(true, new List<ColumnaOrigen>(), null));

        var res = await ctrl.Probar(ds.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(res);
        Assert.True(tester.ArchivoLlamado);
        Assert.False(tester.SqlLlamado);
    }
```

- [ ] **Step 2: Correr los tests — deben fallar**

Run: `dotnet test tests/DataPipeline.Api.Tests --filter "DataSourcesColumnasTests" --nologo`
Expected: FAIL — `Columnas(id, null, ct)` no compila (`ColumnasReq` no acepta `null` como segundo overload todavia / `Sql` no es nullable) y `Probar` no ramifica por `Tipo` todavia.

- [ ] **Step 3: Reescribir `DataSourcesController.cs`**

Reemplazar el archivo completo:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): DataSourceUpsert
// suma los campos de carpeta (reemplaza FilePath, sin uso); Probar/Columnas se ramifican por Tipo.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataPipeline.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = Auth.BasicAuthHandler.Scheme, Roles = Auth.BasicAuthHandler.RolAdmin)]
[Route("api/datasources")]
public sealed class DataSourcesController : ControllerBase
{
    private readonly IDataSourceStore _store;
    private readonly IEncryptionService _enc;
    private readonly IConnectionTester _tester;
    private readonly ISchemaDiscoverer _discoverer;

    public DataSourcesController(IDataSourceStore store, IEncryptionService enc, IConnectionTester tester, ISchemaDiscoverer discoverer)
    {
        _store = store;
        _enc = enc;
        _tester = tester;
        _discoverer = discoverer;
    }

    public sealed record DataSourceVista(Guid Id, string Nombre, TipoDataSource Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel);

    public sealed record DataSourceUpsert(Guid Id, string Nombre, TipoDataSource Tipo,
        string? ConnectionString, string? Username, string? Password, string? BaseUrl,
        string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
        bool TieneEncabezado, string? HojaExcel, bool Activo);

    public sealed record ColumnasReq(string? Sql);

    private static DataSourceVista AVista(DataSource d) =>
        new(d.Id, d.Nombre, d.Tipo, d.Activo, d.CreatedAt, d.UpdatedAt, !string.IsNullOrEmpty(d.ConnectionString),
            d.RutaCarpeta, d.PatronArchivo, d.Separador, d.Encoding, d.TieneEncabezado, d.HojaExcel);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok((await _store.GetAllAsync(ct)).Select(AVista));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        return d is null ? NotFound() : Ok(AVista(d));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] DataSourceUpsert req, CancellationToken ct)
    {
        var id = req.Id == Guid.Empty ? Guid.NewGuid() : req.Id;
        var existente = req.Id == Guid.Empty ? null : await _store.GetByIdAsync(id, ct);

        // Secreto vacio en update = conservar el anterior (evita borrarlo al editar otros campos).
        string? Secreto(string? plano, string? anterior) =>
            string.IsNullOrEmpty(plano) ? anterior : _enc.Encrypt(plano);

        var ds = new DataSource
        {
            Id = id,
            Nombre = req.Nombre,
            Tipo = req.Tipo,
            ConnectionString = Secreto(req.ConnectionString, existente?.ConnectionString),
            Username = Secreto(req.Username, existente?.Username),
            Password = Secreto(req.Password, existente?.Password),
            BaseUrl = req.BaseUrl,
            RutaCarpeta = req.RutaCarpeta,
            PatronArchivo = req.PatronArchivo,
            Separador = req.Separador,
            Encoding = req.Encoding,
            TieneEncabezado = req.TieneEncabezado,
            HojaExcel = req.HojaExcel,
            Activo = req.Activo,
            CreatedAt = existente?.CreatedAt ?? DateTime.Now,
            UpdatedAt = existente is null ? null : DateTime.Now
        };
        await _store.UpsertAsync(ds, ct);
        return Ok(new { ds.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var borrado = await _store.DeleteAsync(id, ct);
        return borrado ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/probar")]
    public async Task<IActionResult> Probar(Guid id, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();

        if (d.Tipo != TipoDataSource.SqlServer)
        {
            var rArchivo = await _tester.ProbarArchivoAsync(d.RutaCarpeta ?? "", ct);
            return Ok(new { ok = rArchivo.Ok, error = rArchivo.Error });
        }

        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _tester.ProbarSqlServerAsync(cs, ct);
        return Ok(new { ok = r.Ok, error = r.Error });
    }

    [HttpPost("{id:guid}/columnas")]
    public async Task<IActionResult> Columnas(Guid id, [FromBody] ColumnasReq? req, CancellationToken ct)
    {
        var d = await _store.GetByIdAsync(id, ct);
        if (d is null) return NotFound();

        if (d.Tipo != TipoDataSource.SqlServer)
        {
            var rArchivo = await _discoverer.DescubrirArchivoAsync(d, ct);
            return rArchivo.Ok ? Ok(rArchivo.Columnas) : BadRequest(new { error = rArchivo.Error });
        }

        if (string.IsNullOrWhiteSpace(req?.Sql)) return BadRequest(new { error = "SQL vacio." });
        var cs = string.IsNullOrEmpty(d.ConnectionString) ? "" : _enc.Decrypt(d.ConnectionString);
        var r = await _discoverer.DescubrirSqlServerAsync(cs, req.Sql, ct);
        return r.Ok ? Ok(r.Columnas) : BadRequest(new { error = r.Error });
    }
}
```

- [ ] **Step 4: Correr los tests — deben pasar**

Run: `dotnet test tests/DataPipeline.Api.Tests --nologo`
Expected: PASS (toda la suite Api, incluidos `DataSourcesApiTests` — el JSON de sus records locales matchea por nombre de propiedad, no se rompe por los campos nuevos/quitados).

- [ ] **Step 5: Build limpio de la solucion completa**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). `DataPipeline.Api` ya compila limpio; `DataPipeline.Admin` sigue roto (Task 9/10 lo arreglan) — anotarlo si es el unico error restante.

- [ ] **Step 6: Commit**

```bash
git add DataPipeline.Api/Controllers/DataSourcesController.cs tests/DataPipeline.Api.Tests/DataSourcesColumnasTests.cs
git commit -m "$(cat <<'EOF'
feat(api): DataSourcesController — campos de archivo + Probar/Columnas ramificados por Tipo

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Admin — pantalla "Origenes de datos" (campos condicionales por Tipo)

**Files:**
- Modify: `DataPipeline.Admin/Services/DataSourcesAdminService.cs`
- Modify: `DataPipeline.Admin/Components/Pages/DataSources/Index.razor`

**Interfaces:**
- Consumes: `DataSourcesController` (Task 8).
- Produces: `DataSourceVista`/`DataSourceUpsert` (Admin-side DTOs) con los 6 campos nuevos; `TraerColumnasAsync(Guid id, string? sql)`. Consumido por `Editor.razor` (Task 10).

**Nota:** build-only (Blazor/Syncfusion) — sin unit tests, por convencion del repo. Verificacion = build limpio + prueba manual. Este task arregla el resto del break de `DataPipeline.Admin` dejado por Task 1.

- [ ] **Step 1: Actualizar los DTOs en `DataSourcesAdminService.cs`**

Reemplazar las 2 primeras lineas de records y el metodo `TraerColumnasAsync`:

```csharp
// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): campos de carpeta
// en los DTOs (reemplazan FilePath, sin uso); TraerColumnasAsync no exige sql para archivo.
/// <summary>Vista de un DataSource (sin secretos; calca el shape que devuelve la API).</summary>
public sealed record DataSourceVista(Guid Id, string Nombre, int Tipo, bool Activo,
    DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString,
    string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
    bool TieneEncabezado, string? HojaExcel);

/// <summary>Payload de alta/edicion (secretos en texto plano; vacio en edicion = conservar).</summary>
public sealed record DataSourceUpsert(Guid Id, string Nombre, int Tipo, string? ConnectionString,
    string? Username, string? Password, string? BaseUrl,
    string? RutaCarpeta, string? PatronArchivo, string? Separador, string? Encoding,
    bool TieneEncabezado, string? HojaExcel, bool Activo);
```

Y `TraerColumnasAsync`:

```csharp
    public async Task<List<ColumnaOrigenDto>?> TraerColumnasAsync(Guid id, string? sql)
    {
        var r = await _http.PostAsJsonAsync($"api/datasources/{id}/columnas", new { sql });
        if (!r.IsSuccessStatusCode) return null; // null = error (SQL invalido, carpeta sin archivos, etc.)
        return JsonSerializer.Deserialize<List<ColumnaOrigenDto>>(await r.Content.ReadAsStringAsync(), _json) ?? new();
    }
```

- [ ] **Step 2: Actualizar `DataSources/Index.razor`**

`_tipos` pasa a:

```csharp
    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo: Csv/Xlsx (mismo orden que TipoDataSource).
    private readonly List<OpcionTipo> _tipos = new() { new(0, "SqlServer"), new(1, "Csv"), new(2, "Xlsx") };
```

`FormModel` suma los 6 campos:

```csharp
    private sealed class FormModel
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public int Tipo { get; set; }
        public string? ConnectionString { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx).
        public string? RutaCarpeta { get; set; }
        public string? PatronArchivo { get; set; }
        public string Separador { get; set; } = ",";
        public string Encoding { get; set; } = "UTF-8";
        public bool TieneEncabezado { get; set; } = true;
        public string? HojaExcel { get; set; }
        public bool Activo { get; set; } = true;
    }
```

`TipoTexto` pasa a:

```csharp
    private static string TipoTexto(int tipo) => tipo switch { 0 => "SqlServer", 1 => "Csv", 2 => "Xlsx", _ => tipo.ToString() };
```

En el dialogo de alta/edicion, reemplazar el bloque que va desde el `SfDropDownList` de Tipo hasta el checkbox "Activo" — insertar los campos condicionales entre el dropdown de Tipo y el boton "Mostrar/Ocultar secretos":

```razor
            <div class="mb-3">
                <SfDropDownList TValue="int" TItem="OpcionTipo" DataSource="_tipos" @bind-Value="_form.Tipo"
                                Placeholder="Tipo" FloatLabelType="FloatLabelType.Always">
                    <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                </SfDropDownList>
            </div>

            @* Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): campos condicionales por Tipo. *@
            @if (_form.Tipo == 1 || _form.Tipo == 2)
            {
                <div class="mb-2">
                    <SfTextBox Placeholder="Carpeta *" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.RutaCarpeta" />
                </div>
                <div class="mb-2">
                    <SfTextBox Placeholder="@(_form.Tipo == 1 ? "Patron de archivo (ej. VENTAS_*.csv)" : "Patron de archivo (ej. VENTAS_*.xlsx)")"
                               FloatLabelType="FloatLabelType.Always" @bind-Value="_form.PatronArchivo" />
                </div>
            }
            @if (_form.Tipo == 1)
            {
                <div class="mb-2">
                    <SfDropDownList TValue="string" TItem="OpcionTexto" DataSource="_separadores" @bind-Value="_form.Separador"
                                    Placeholder="Separador" FloatLabelType="FloatLabelType.Always">
                        <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                    </SfDropDownList>
                </div>
                <div class="mb-2">
                    <SfDropDownList TValue="string" TItem="OpcionTexto" DataSource="_encodings" @bind-Value="_form.Encoding"
                                    Placeholder="Encoding" FloatLabelType="FloatLabelType.Always">
                        <DropDownListFieldSettings Value="Valor" Text="Texto"></DropDownListFieldSettings>
                    </SfDropDownList>
                </div>
                <div class="mb-2">
                    <SfCheckBox @bind-Checked="_form.TieneEncabezado" Label="Tiene encabezado"></SfCheckBox>
                </div>
            }
            @if (_form.Tipo == 2)
            {
                <div class="mb-2">
                    <SfTextBox Placeholder="Hoja (vacio = primera)" FloatLabelType="FloatLabelType.Always" @bind-Value="_form.HojaExcel" />
                </div>
            }

            @if (_form.Tipo == 0)
            {
                <div class="d-flex mb-1" style="justify-content:flex-end;">
                    <SfButton CssClass="e-flat e-small" OnClick="@(() => _mostrarSecretos = !_mostrarSecretos)">
                        @(_mostrarSecretos ? "Ocultar secretos" : "Mostrar secretos")
                    </SfButton>
                </div>

                <div class="mb-2">
                    <SfTextBox Placeholder="@(_form.Id == Guid.Empty ? "Connection string *" : "Connection string")"
                               FloatLabelType="FloatLabelType.Always"
                               Type="@(_mostrarSecretos ? InputType.Text : InputType.Password)"
                               @bind-Value="_form.ConnectionString" />
                </div>
                <div class="mb-2">
                    <SfTextBox Placeholder="Usuario" FloatLabelType="FloatLabelType.Always"
                               Type="@(_mostrarSecretos ? InputType.Text : InputType.Password)"
                               @bind-Value="_form.Username" />
                </div>
                <div class="mb-2">
                    <SfTextBox Placeholder="Contrasena" FloatLabelType="FloatLabelType.Always"
                               Type="@(_mostrarSecretos ? InputType.Text : InputType.Password)"
                               @bind-Value="_form.Password" />
                </div>

                @if (_form.Id != Guid.Empty)
                {
                    <small class="texto-secundario d-block mb-2">Deja los secretos vacios para conservar los actuales.</small>
                }
            }
```

Agregar `OpcionTexto` y las dos listas de opciones junto a `_tipos`:

```csharp
    private sealed record OpcionTexto(string Valor, string Texto);
    private readonly List<OpcionTexto> _separadores = new() { new(",", ","), new(";", ";"), new("\t", "Tab") };
    private readonly List<OpcionTexto> _encodings = new() { new("UTF-8", "UTF-8"), new("Windows-1252", "Windows-1252 (ANSI/Latin1)") };
```

En `NuevoOrigen()`, agregar los defaults:

```csharp
    private void NuevoOrigen()
    {
        _form = new FormModel();
        _mostrarSecretos = false;
        _errorNombre = _errorForm = null;
        _formAbierto = true;
    }
```

(sin cambios: `FormModel` ya trae los defaults `Separador = ","`, `Encoding = "UTF-8"`, `TieneEncabezado = true` desde su declaracion).

En `Editar(Guid id)`, sumar la carga de los campos no-secretos:

```csharp
    private async Task Editar(Guid id)
    {
        var d = await Origenes.ObtenerPorIdAsync(id);
        if (d is null) { Mostrar(false, "No se encontro el origen."); return; }
        // Secretos NO se pre-cargan (la API no los devuelve); vacio = conservar al guardar.
        _form = new FormModel
        {
            Id = d.Id, Nombre = d.Nombre, Tipo = d.Tipo, Activo = d.Activo,
            RutaCarpeta = d.RutaCarpeta, PatronArchivo = d.PatronArchivo,
            Separador = string.IsNullOrEmpty(d.Separador) ? "," : d.Separador,
            Encoding = string.IsNullOrEmpty(d.Encoding) ? "UTF-8" : d.Encoding,
            TieneEncabezado = d.TieneEncabezado, HojaExcel = d.HojaExcel
        };
        _mostrarSecretos = false;
        _errorNombre = _errorForm = null;
        _formAbierto = true;
    }
```

En `Guardar()`, la validacion y el `DataSourceUpsert`:

```csharp
    private async Task Guardar()
    {
        _errorNombre = _errorForm = null;
        if (string.IsNullOrWhiteSpace(_form.Nombre)) { _errorNombre = "El nombre es obligatorio."; return; }
        if (_form.Tipo == 0 && _form.Id == Guid.Empty && string.IsNullOrWhiteSpace(_form.ConnectionString))
        {
            _errorForm = "La connection string es obligatoria para un origen nuevo.";
            return;
        }
        if (_form.Tipo != 0 && string.IsNullOrWhiteSpace(_form.RutaCarpeta))
        {
            _errorForm = "La carpeta es obligatoria.";
            return;
        }

        _guardando = true;
        try
        {
            var dto = new DataSourceUpsert(_form.Id, _form.Nombre.Trim(), _form.Tipo,
                _form.ConnectionString, _form.Username, _form.Password, null,
                _form.RutaCarpeta, _form.PatronArchivo, _form.Separador, _form.Encoding,
                _form.TieneEncabezado, _form.HojaExcel, _form.Activo);
            var ok = await Origenes.GuardarAsync(dto);
            if (ok)
            {
                _formAbierto = false;
                await Recargar();
                Mostrar(true, "Origen guardado.");
            }
            else
            {
                _errorForm = "No se pudo guardar (revisa la API o el nombre duplicado).";
            }
        }
        finally { _guardando = false; }
    }
```

- [ ] **Step 3: Build limpio de la solucion completa**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). Este es el primer punto del plan donde **toda** la solucion vuelve a compilar limpia (Editor.razor todavia no usa los campos nuevos de `DataSourceVista`, pero eso no rompe el build — se completa en Task 10).

- [ ] **Step 4: Prueba manual**

Correr el Admin, ir a "Origenes de datos": crear un origen `Csv` (carpeta + patron + separador `;` + encoding + encabezado) y un origen `Xlsx` (carpeta + patron + hoja); confirmar que "Probar" da resultado (aunque la carpeta no exista todavia, no debe tirar excepcion, solo `ok:false`); editar cada uno y confirmar que los campos no-secretos quedan pre-cargados.

- [ ] **Step 5: Commit**

```bash
git add DataPipeline.Admin/Services/DataSourcesAdminService.cs DataPipeline.Admin/Components/Pages/DataSources/Index.razor
git commit -m "$(cat <<'EOF'
feat(admin): pantalla Origenes de datos con campos condicionales para Csv/Xlsx

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Admin — wizard del Editor de pipelines (Paso 1 reacciona al Tipo del origen)

**Files:**
- Modify: `DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor`

**Interfaces:**
- Consumes: `DataSourceVista.Tipo` (Task 9), `PipelineDefinition.ColumnaWatermarkTipo` (Task 1, ya disponible via `DataPipeline.Core.Pipelines` que Editor.razor referencia directamente).

**Nota:** build-only, sin unit tests (convencion del repo).

- [ ] **Step 1: Agregar `ColumnaWatermarkTipo` al `PipelineForm` y a `_tiposCast` ya existente**

En `@code`, agregar el campo al `PipelineForm` (junto a `ColumnaWatermark`):

```csharp
        public string? ColumnaWatermark { get; set; } = ""; // columna del origen a trackear; vacio = no incremental
        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Solo aplica a origenes Csv/Xlsx (SqlServer ya viene tipado).
        public string? ColumnaWatermarkTipo { get; set; } = "";
```

- [ ] **Step 2: Helper para saber si el origen elegido es de archivo**

Agregar junto a `NombresColumnas`:

```csharp
    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origen de archivo = no hay SQL; hay tipo de watermark.
    private int? TipoDataSourceElegido =>
        _datasources.FirstOrDefault(d => d.Id == _form.DataSourceId)?.Tipo;

    private bool EsOrigenDeArchivo => TipoDataSourceElegido is 1 or 2; // Csv=1, Xlsx=2 (ver DataSourcesAdminService)
```

- [ ] **Step 3: Paso 1 — ocultar el SQL y agregar el dropdown de tipo de watermark para origenes de archivo**

Reemplazar el bloque del Paso 1 (desde el `<div class="mb-1">` del SQL hasta el cierre del `@if (_form.EsquemaOrigen.Count > 0)`):

```razor
                @if (!EsOrigenDeArchivo)
                {
                    <div class="mb-1">
                        <SfTextBox Placeholder="SQL *" FloatLabelType="FloatLabelType.Always" Multiline="true" @bind-Value="_form.Sql" />
                    </div>
                }
                else
                {
                    <div class="texto-caption texto-secundario mb-1">
                        Origen de archivo: no hace falta SQL. "Traer columnas" lee el encabezado del archivo mas nuevo sin procesar.
                    </div>
                }
                <div class="mt-2 mb-3 d-flex align-center gap-2">
                    <SfButton CssClass="e-small" OnClick="TraerColumnas"
                              Disabled="@(_form.DataSourceId is null || (!EsOrigenDeArchivo && string.IsNullOrWhiteSpace(_form.Sql)))">
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
                        @if (!EsOrigenDeArchivo)
                        {
                            <div class="texto-caption texto-secundario mt-1">
                                Tu SQL debe filtrar con @@watermark (ej. WHERE @@watermark IS NULL OR fecha_mod &gt; @@watermark).
                                Si la columna es fecha, debe ser datetime/datetime2.
                            </div>
                        }
                    </div>
                    @if (EsOrigenDeArchivo && !string.IsNullOrWhiteSpace(_form.ColumnaWatermark))
                    {
                        <div class="mb-1">
                            <SfDropDownList TValue="string" TItem="string" DataSource="_tiposCast" @bind-Value="_form.ColumnaWatermarkTipo"
                                            Placeholder="Tipo de columna watermark" FloatLabelType="FloatLabelType.Always" />
                            <div class="texto-caption texto-secundario mt-1">
                                Necesario para comparar bien un archivo (texto plano): sin esto, una fecha o un numero se compara como texto.
                            </div>
                        </div>
                    }
                }
```

- [ ] **Step 4: `TraerColumnas()` no exige SQL para origenes de archivo**

```csharp
    private async Task TraerColumnas()
    {
        _columnasMsg = "Trayendo...";
        var cols = await Origenes.TraerColumnasAsync(_form.DataSourceId!.Value, EsOrigenDeArchivo ? null : _form.Sql);
        if (cols is null)
        {
            _columnasMsg = "No se pudieron traer las columnas (revisa el SQL/la conexion, o que haya archivos en la carpeta).";
            return;
        }
        _form.EsquemaOrigen = cols;
        _columnasMsg = $"{cols.Count} columnas.";
    }
```

- [ ] **Step 5: `ConstruirDef()` arma el `Conector` segun el tipo del DataSource**

Reemplazar el bloque de armado de `pasos`:

```csharp
    private PipelineDefinition ConstruirDef()
    {
        var conector = TipoDataSourceElegido switch { 1 => "CsvSource", 2 => "XlsxSource", _ => "SqlServerSource" };
        var cfgOrigen = new Dictionary<string, JsonElement>
        {
            ["dataSourceId"] = JsonSerializer.SerializeToElement(_form.DataSourceId?.ToString() ?? "")
        };
        if (conector == "SqlServerSource")
            cfgOrigen["sql"] = JsonSerializer.SerializeToElement(_form.Sql);

        var pasos = new List<StepDefinition>
        {
            new()
            {
                Nombre = "origen",
                Tipo = TipoPaso.Origen,
                Conector = conector,
                Configuracion = cfgOrigen
            }
        };
```

(El resto del metodo, desde `foreach (var t in _form.Transforms)` hasta el final, no cambia — salvo agregar `ColumnaWatermarkTipo` al `return new PipelineDefinition { ... }`:)

```csharp
            ColumnaWatermark = string.IsNullOrWhiteSpace(_form.ColumnaWatermark)
                ? null
                : _form.ColumnaWatermark!.Trim(),
            // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1
            ColumnaWatermarkTipo = string.IsNullOrWhiteSpace(_form.ColumnaWatermarkTipo)
                ? null
                : _form.ColumnaWatermarkTipo!.Trim(),
            Pasos = pasos
```

- [ ] **Step 6: `Guardar()` valida SQL solo para SqlServer**

```csharp
        if (_form.DataSourceId is null || (!EsOrigenDeArchivo && string.IsNullOrWhiteSpace(_form.Sql)))
        {
            _error = "El origen requiere un DataSource" + (EsOrigenDeArchivo ? "." : " y un SQL.");
            return;
        }
```

(reemplaza la condicion equivalente existente en `Guardar()`).

- [ ] **Step 7: `Cargar(PipelineDefinition def)` lee `ColumnaWatermarkTipo`**

```csharp
        _form.ColumnaWatermark = def.ColumnaWatermark ?? "";
        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1
        _form.ColumnaWatermarkTipo = def.ColumnaWatermarkTipo ?? "";
        _form.FilasPorLote = def.FilasPorLote;
```

- [ ] **Step 8: Build limpio de la solucion completa**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). **Toda** la solucion compila limpia desde este punto.

- [ ] **Step 9: Prueba manual end-to-end**

1. Crear un `DataSource` Csv apuntando a una carpeta de prueba (crear la carpeta a mano si no existe).
2. Dejar un `ventas.csv` de prueba en esa carpeta (encabezado `id,importe`, un par de filas).
3. Armar un pipeline: Paso 1 elegir ese DataSource, confirmar que NO pide SQL, "Traer columnas" trae `id`/`importe`, elegir `id` como columna watermark y tipo `int`. Paso 2: tabla destino + modo. Guardar.
4. Correr el pipeline (bajo demanda). Confirmar: filas cargadas en la tabla destino, y que `ventas.csv` se movio a la subcarpeta `procesados` dentro de la carpeta configurada.
5. Correr el pipeline de nuevo sin agregar archivos nuevos: debe terminar en 0 filas leidas (nada que procesar).
6. Repetir 1-4 con un `DataSource` Xlsx y un `.xlsx` de prueba.

- [ ] **Step 10: Commit**

```bash
git add DataPipeline.Admin/Components/Pages/Pipelines/Editor.razor
git commit -m "$(cat <<'EOF'
feat(admin): wizard del Editor reacciona al Tipo de origen (Csv/Xlsx sin SQL, tipo de watermark)

Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Verificacion final y cierre de tarea

**Files:**
- Create (si no existe): `Docs/Historial.md`
- Modify: `~/.claude/TAREAS.md`

- [ ] **Step 1: Build completo sin warnings**

Run: `dotnet build --no-incremental --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Suite completa verde**

Run: `dotnet test --nologo`
Expected: todos los tests verdes (Core/Connectors/Engine/Api con cobertura nueva de esta tarea; Admin sin tests, por convencion).

- [ ] **Step 3: Verificacion manual final (si no se hizo ya en Task 10)**

Repetir el flujo end-to-end de Task 10 Step 9 si quedo pendiente.

- [ ] **Step 4: Crear `Docs/Historial.md` (no existe en el repo) y agregar la entrada de esta tarea**

Crear `D:\Sistemas\MiddleWare\Docs\Historial.md`:

```markdown
# Historial — DataPipeline (MiddleWare)

Registro de tareas ejecutadas en este repo, correlativas con `~/.claude/TAREAS.md`.

## TAREA 00014 — 03/09/2026

**Pedido (item 1):** Importar CSV y XLSX para llenar una base de datos con archivos extraidos de un
ERP viejo que no tiene conexion directa a SQL.

**Ejecutado:**
- Dos origenes nuevos (`Csv`, `Xlsx`) que leen de una carpeta de red configurable, con carga
  incremental por columna watermark (tipada) y sin reprocesar archivos ya leidos (se mueven a una
  subcarpeta `\procesados` al terminar sin error).
- `FileSourceBase` (comun) + `CsvSource` (CsvHelper) + `XlsxSource` (ClosedXML, mismo patron de
  lectura que `ExcelQueryExecutor` de ConsultasGenericas).
- `DataSource` gana `RutaCarpeta`/`PatronArchivo`/`Separador`/`Encoding`/`TieneEncabezado`/`HojaExcel`
  (reemplaza el `FilePath` sin uso); `PipelineDefinition` gana `ColumnaWatermarkTipo`.
- Api (`DataSourcesController`, `IConnectionTester`, `ISchemaDiscoverer`) y Admin (pantalla de
  Origenes + wizard del Editor) actualizados para los dos tipos nuevos.
- Fuera de alcance (explicito): MySQL/MariaDB/Oracle/RestApi (existen en el proyecto madre
  ConsultasGenericas pero no se pidieron aca); upload manual de archivos.

Spec: `docs/superpowers/specs/2026-09-03-datapipeline-origenes-archivo-design.md`.
Plan: `docs/superpowers/plans/2026-09-03-datapipeline-origenes-archivo.md`.
```

- [ ] **Step 5: Agregar la fila en `~/.claude/TAREAS.md` y actualizar el marcador**

En `C:\Users\mjulian\.claude\TAREAS.md`, agregar la fila a la tabla (antes del marcador) y mover el marcador:

```markdown
| 00014 | 03/09/2026 | MiddleWare | 1 | Importar CSV y XLSX para llenar una base de datos con archivos extraidos de un ERP viejo sin conexion SQL directa: dos origenes nuevos (Csv/Xlsx) que leen de una carpeta de red, cargan incrementalmente por columna watermark tipada y mueven cada archivo procesado a una subcarpeta `procesados` para no reprocesarlo. Detalle en `D:\Sistemas\MiddleWare\Docs\Historial.md`. |

<!-- Próxima tarea: 00015 -->
```

(reemplaza la linea `<!-- Próxima tarea: 00014 -->` existente al final del archivo).

- [ ] **Step 6: Commit de los archivos de cierre**

```bash
git add Docs/Historial.md
git commit -m "$(cat <<'EOF'
docs(historial): TAREA 00014 — origenes de archivo (CSV/XLSX)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

> `~/.claude/TAREAS.md` esta fuera de este repo (es global, compartido entre proyectos) — no lleva commit de git de MiddleWare; se edita directamente.

---

## Notas para quien ejecute

- **Orden de tasks:** seguir el orden 1→11. Las tasks 1-8 dejan el build de `DataPipeline.Admin` roto hasta la Task 9 (usa `DataSource.FilePath`, eliminado en Task 1) — es esperado y se anota explicitamente en cada task; no es un bug a corregir antes de tiempo.
- **CsvHelper/ClosedXML — versiones:** las versiones puestas en el plan (`CsvHelper 33.0.1`, `ClosedXML 0.104.2`) son las mas recientes conocidas al escribir este plan; si `dotnet restore` resuelve una version distinta (mas nueva, patch), usar la que quede resuelta y no forzarla hacia abajo.
- **Windows-1252:** se resuelve a `Encoding.Latin1` (incluido en .NET 5+, sin paquete adicional) — cubre el caso tipico de un ERP viejo sin necesitar `System.Text.Encoding.CodePages` + `RegisterProvider`. Si en la practica aparecen caracteres especiales (comillas curvas, etc.) que Latin1 no resuelve bien, es una mejora futura, no bloqueante para esta tarea.
- **Diferido / fuera de alcance (igual que el spec):** MySQL/MariaDB/Oracle/RestApi; upload manual de archivos; deteccion automatica de dump-acumulativo-vs-delta (no hace falta); locking entre corridas superpuestas del mismo pipeline (limitacion preexistente).
