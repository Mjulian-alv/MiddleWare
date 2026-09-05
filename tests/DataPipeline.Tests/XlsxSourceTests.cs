// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Tests de XlsxSource con libros reales generados por
// ClosedXML (mismo patron de lectura que ExcelQueryExecutor de ConsultasGenericas: primera fila =
// encabezado), agregando hoja nombrada y watermark.
using System.Globalization;
using ClosedXML.Excel;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

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

    // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final (Hallazgo 1, Critico): estos dos
    // tests usan celdas NUMERICAS/DateTime reales (no strings, a diferencia de los tests de arriba) y
    // fuerzan CurrentCulture = es-AR, que es la combinacion que ninguna review por task detecto: todos
    // los tests de XLSX existentes usaban celdas de texto, asi que .Value.ToString() con formato
    // culture-dependiente nunca se disparaba. Sin el fix de XlsxSource (valor nativo de la celda en vez
    // de ToString()), el primer test filtraba mal (10,5 -> 105) y el segundo tiraba FormatException.
    private static async Task<T> ConCulturaEsAR<T>(Func<Task<T>> accion)
    {
        var hiloAnterior = System.Threading.Thread.CurrentThread.CurrentCulture;
        var hiloAnteriorUi = System.Threading.Thread.CurrentThread.CurrentUICulture;
        var esAr = new CultureInfo("es-AR");
        System.Threading.Thread.CurrentThread.CurrentCulture = esAr;
        System.Threading.Thread.CurrentThread.CurrentUICulture = esAr;
        try { return await accion(); }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = hiloAnterior;
            System.Threading.Thread.CurrentThread.CurrentUICulture = hiloAnteriorUi;
        }
    }

    [Fact]
    public async Task Bajo_cultura_es_AR_una_celda_numerica_real_se_tipa_y_filtra_bien_como_decimal()
    {
        var carpeta = NuevaCarpeta();
        var ruta = Path.Combine(carpeta, "ventas.xlsx");
        using (var libro = new XLWorkbook())
        {
            var hoja = libro.Worksheets.Add("Datos");
            hoja.Cell(1, 1).Value = "id"; hoja.Cell(1, 2).Value = "monto";
            hoja.Cell(2, 1).Value = "1"; hoja.Cell(2, 2).Value = 10.5;   // celda NUMERICA real, no string
            hoja.Cell(3, 1).Value = "2"; hoja.Cell(3, 2).Value = 20.5;
            libro.SaveAs(ruta);
        }
        var source = new XlsxSource(carpeta, "*.xlsx", "monto", "decimal", null);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "10.5" });

        var filas = await ConCulturaEsAR(() => Drenar(source.ReadAsync(ctx, CancellationToken.None)));

        // Antes del fix: "10,5" (formateado con la cultura de la maquina) se reparseaba con
        // InvariantCulture como 105, asi que las dos filas pasaban el filtro (105 > 10.5 y 20.5 > 10.5).
        Assert.Single(filas);
        Assert.Equal("2", filas[0].Values["id"]);
    }

    [Fact]
    public async Task Bajo_cultura_es_AR_una_celda_de_fecha_real_con_dia_mayor_a_12_no_explota()
    {
        var carpeta = NuevaCarpeta();
        var ruta = Path.Combine(carpeta, "ventas.xlsx");
        using (var libro = new XLWorkbook())
        {
            var hoja = libro.Worksheets.Add("Datos");
            hoja.Cell(1, 1).Value = "id"; hoja.Cell(1, 2).Value = "fecha";
            hoja.Cell(2, 1).Value = "1"; hoja.Cell(2, 2).Value = new DateTime(2026, 9, 10); // dia <= 12
            hoja.Cell(3, 1).Value = "2"; hoja.Cell(3, 2).Value = new DateTime(2026, 9, 20); // dia > 12: crasheaba antes del fix
            libro.SaveAs(ruta);
        }
        var source = new XlsxSource(carpeta, "*.xlsx", "fecha", "date", null);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "2026-09-10T00:00:00.0000000" });

        var filas = await ConCulturaEsAR(() => Drenar(source.ReadAsync(ctx, CancellationToken.None)));

        Assert.Single(filas);
        Assert.Equal("2", filas[0].Values["id"]);
    }
}
