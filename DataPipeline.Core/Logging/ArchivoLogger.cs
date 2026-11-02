// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Log a archivo.
// Hasta ahora los errores solo existian en la consola de la ventana que levanta el servicio: si la
// ventana no estaba a la vista, o el error pasaba mientras nadie miraba, no quedaba rastro de nada.
// Esto es un proveedor de logging propio, sin dependencias nuevas mas que las Abstractions: escribe
// una linea por evento en logs/<prefijo>-yyyyMMdd.log, rota por tamano y borra los viejos.
//
// Regla de oro de un logger: NUNCA tirar. Si no puede escribir, se traga el problema; que falle el
// log no puede ser lo que voltee la aplicacion que se esta tratando de diagnosticar.
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataPipeline.Core.Logging;

/// <summary>Opciones del log a archivo. Todas tienen un valor razonable por defecto.</summary>
public sealed class ArchivoLoggerOptions
{
    /// <summary>Carpeta donde se escriben los .log. Relativa al directorio de la aplicacion.</summary>
    public string Carpeta { get; set; } = "logs";

    /// <summary>Prefijo del archivo: "dp-api" produce "dp-api-20261101.log".</summary>
    public string Prefijo { get; set; } = "dp";

    /// <summary>Nivel minimo que se escribe al archivo.</summary>
    public LogLevel Minimo { get; set; } = LogLevel.Information;

    /// <summary>Tamano maximo por archivo antes de abrir uno nuevo con sufijo.</summary>
    public int MaxArchivoMB { get; set; } = 10;

    /// <summary>Los .log mas viejos que esto se borran al arrancar.</summary>
    public int DiasAConservar { get; set; } = 30;
}

/// <summary>Escribe las lineas al archivo del dia. Una sola instancia, compartida por categoria.</summary>
internal sealed class EscritorArchivo : IDisposable
{
    private readonly ArchivoLoggerOptions _op;
    private readonly object _candado = new();
    private readonly string _carpeta;
    private DateTime _diaActual = DateTime.MinValue;
    private string? _rutaActual;
    private bool _avisoDeFalloDado;

    public EscritorArchivo(ArchivoLoggerOptions op)
    {
        _op = op;
        _carpeta = Path.IsPathRooted(op.Carpeta)
            ? op.Carpeta
            : Path.Combine(AppContext.BaseDirectory, op.Carpeta);

        try
        {
            Directory.CreateDirectory(_carpeta);
            BorrarViejos();
        }
        catch (Exception ex)
        {
            Avisar(ex);
        }
    }

    public void Escribir(string linea)
    {
        lock (_candado)
        {
            try
            {
                var ruta = RutaDelDia();
                File.AppendAllText(ruta, linea + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Avisar(ex);
            }
        }
    }

    /// <summary>Devuelve el archivo del dia, rotando si se paso del tamano maximo.</summary>
    private string RutaDelDia()
    {
        var hoy = DateTime.Now.Date;
        if (_rutaActual is not null && hoy == _diaActual && !SePaso(_rutaActual))
            return _rutaActual;

        _diaActual = hoy;
        var baseNombre = $"{_op.Prefijo}-{hoy:yyyyMMdd}";
        var candidata = Path.Combine(_carpeta, baseNombre + ".log");

        // Si la del dia ya es grande, se abre la siguiente: -1, -2, ...
        var n = 1;
        while (SePaso(candidata))
            candidata = Path.Combine(_carpeta, $"{baseNombre}-{n++}.log");

        _rutaActual = candidata;
        return candidata;
    }

    private bool SePaso(string ruta)
    {
        try
        {
            var fi = new FileInfo(ruta);
            return fi.Exists && fi.Length >= (long)_op.MaxArchivoMB * 1024 * 1024;
        }
        catch
        {
            return false;
        }
    }

    private void BorrarViejos()
    {
        if (_op.DiasAConservar <= 0) return;
        var limite = DateTime.Now.AddDays(-_op.DiasAConservar);

        foreach (var f in Directory.EnumerateFiles(_carpeta, $"{_op.Prefijo}-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(f) < limite) File.Delete(f);
            }
            catch
            {
                // Un archivo tomado por otro proceso no es motivo para frenar la limpieza.
            }
        }
    }

    /// <summary>Un solo aviso por consola si el log a archivo no funciona, para no inundar.</summary>
    private void Avisar(Exception ex)
    {
        if (_avisoDeFalloDado) return;
        _avisoDeFalloDado = true;
        Console.Error.WriteLine($"[log a archivo deshabilitado] {ex.GetType().Name}: {ex.Message}");
    }

    public void Dispose() { }
}

/// <summary>Logger de una categoria que escribe al archivo compartido.</summary>
internal sealed class ArchivoLogger : ILogger
{
    private readonly string _categoria;
    private readonly EscritorArchivo _escritor;
    private readonly ArchivoLoggerOptions _op;

    public ArchivoLogger(string categoria, EscritorArchivo escritor, ArchivoLoggerOptions op)
    {
        _categoria = categoria;
        _escritor = escritor;
        _op = op;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel nivel) => nivel != LogLevel.None && nivel >= _op.Minimo;

    public void Log<TState>(LogLevel nivel, EventId evento, TState state, Exception? ex,
                            Func<TState, Exception?, string> formatear)
    {
        if (!IsEnabled(nivel)) return;

        var sb = new StringBuilder(256);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
          .Append(" [").Append(Etiqueta(nivel)).Append("] ")
          .Append(_categoria);

        if (evento.Id != 0) sb.Append('[').Append(evento.Id).Append(']');
        sb.Append(' ').Append(formatear(state, ex));

        // La excepcion va completa, con el stack y las internas: es justo lo que hace falta leer
        // cuando algo fallo y la pantalla solo dijo "ocurrio un error".
        if (ex is not null)
            sb.Append(Environment.NewLine).Append(ex);

        _escritor.Escribir(sb.ToString());
    }

    private static string Etiqueta(LogLevel n) => n switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRI",
        _ => "???"
    };
}

/// <summary>Proveedor del log a archivo. Se registra con <c>AddArchivo()</c>.</summary>
public sealed class ArchivoLoggerProvider : ILoggerProvider
{
    private readonly ArchivoLoggerOptions _op;
    private readonly EscritorArchivo _escritor;

    public ArchivoLoggerProvider(ArchivoLoggerOptions op)
    {
        _op = op;
        _escritor = new EscritorArchivo(op);
    }

    public ILogger CreateLogger(string categoryName) => new ArchivoLogger(categoryName, _escritor, _op);

    public void Dispose() => _escritor.Dispose();
}

/// <summary>Registra el log a archivo en el pipeline de logging.</summary>
public static class ArchivoLoggerExtensions
{
    public static ILoggingBuilder AddArchivo(this ILoggingBuilder builder, Action<ArchivoLoggerOptions>? configurar = null)
    {
        var op = new ArchivoLoggerOptions();
        configurar?.Invoke(op);
        // Se registra como servicio y no con AddProvider: asi alcanza con las Abstractions y Core no
        // tiene que arrastrar el paquete completo de logging.
        builder.Services.AddSingleton<ILoggerProvider>(new ArchivoLoggerProvider(op));
        return builder;
    }
}
