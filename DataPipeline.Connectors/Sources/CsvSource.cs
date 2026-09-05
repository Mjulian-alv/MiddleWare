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

        // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final: un archivo vacio hace que
        // csv.Read() devuelva false; sin este guard, ReadHeader() tira ReaderException y el archivo
        // nunca se mueve a \procesados (queda trabado, fallando en cada corrida siguiente).
        if (!csv.Read()) return Array.Empty<string>();
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
            // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final: mismo guard que en
            // LeerEncabezado para un archivo vacio (ver comentario ahi).
            if (!await csv.ReadAsync()) yield break;
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
