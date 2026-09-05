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
            // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final: una celda en blanco (CSV
            // vacio o Excel vacio) en la columna watermark llega como "" (no null); sin este chequeo,
            // TipoValorConverter.Convertir("", ...) tira FormatException y tumba toda la corrida.
            // Se trata igual que "sin watermark configurado": la fila se sigue emitiendo sin tipar/filtrar.
            if (valores.TryGetValue(claveWatermark, out var crudo) && crudo is not null && crudo is not string { Length: 0 })
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
