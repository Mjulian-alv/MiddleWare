// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origen Xlsx: misma libreria y patron de lectura
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
