// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Tests de XlsxSource con libros reales generados por
// ClosedXML (mismo patron de lectura que ExcelQueryExecutor de ConsultasGenericas: primera fila =
// encabezado), agregando hoja nombrada y watermark.
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
}
