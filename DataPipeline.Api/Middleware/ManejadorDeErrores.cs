// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Que un error de la API diga algo.
// Antes, una excepcion no atrapada salia como un 500 sin cuerpo. El Admin veia "no fue 2xx" y
// mostraba un texto generico inventado por el, que no describia nada: el motivo real moria ahi.
//
// Ahora toda excepcion se loguea completa (con stack) y vuelve como JSON { error, traceId }. El
// traceId es la pieza que faltaba: es el mismo numero que se ve en pantalla y en el archivo de log,
// asi que deja de hacer falta adivinar cual de los errores del log es el que se acaba de ver.
using System.Text.Json;

namespace DataPipeline.Api.Middleware;

public sealed class ManejadorDeErrores
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ManejadorDeErrores> _log;
    private readonly IHostEnvironment _entorno;

    public ManejadorDeErrores(RequestDelegate siguiente, ILogger<ManejadorDeErrores> log, IHostEnvironment entorno)
    {
        _siguiente = siguiente;
        _log = log;
        _entorno = entorno;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _siguiente(ctx);
        }
        catch (Exception ex)
        {
            var traceId = ctx.TraceIdentifier;

            _log.LogError(ex,
                "Fallo {Metodo} {Ruta} (traceId {TraceId}): {Mensaje}",
                ctx.Request.Method, ctx.Request.Path, traceId, ex.Message);

            if (ctx.Response.HasStarted)
            {
                // Ya se empezo a escribir la respuesta: no se puede cambiar. Queda en el log.
                _log.LogWarning("La respuesta de {TraceId} ya habia empezado: no se pudo informar el error al cliente.", traceId);
                return;
            }

            ctx.Response.Clear();
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json; charset=utf-8";

            // En Development va el detalle; en Production, el mensaje y el traceId. El stack no sale
            // nunca al cliente: para eso esta el archivo de log, que no lo ve cualquiera.
            var cuerpo = new
            {
                error = _entorno.IsDevelopment()
                    ? $"{ex.GetType().Name}: {ex.Message}"
                    : Describir(ex),
                traceId
            };

            await ctx.Response.WriteAsync(JsonSerializer.Serialize(cuerpo));
        }
    }

    /// <summary>
    /// Un mensaje que sirva para actuar. No se expone el detalle interno, pero tampoco se devuelve
    /// "ocurrio un error": las causas que el usuario PUEDE arreglar se nombran.
    /// </summary>
    public static string Describir(Exception ex)
    {
        // Las causas de base se reconocen por el texto y no por el tipo, para no atar el middleware
        // al proveedor: hoy es SQLite, manana puede ser otro y el mensaje tiene que seguir saliendo.
        var cadena = Encadenado(ex);

        if (cadena.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            return "Ya existe un registro con ese nombre. Los nombres no se pueden repetir.";
        if (cadena.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
            return "Hay algo que depende de este registro, asi que no se puede completar la operacion.";
        if (cadena.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
            return "La base esta ocupada por otro proceso. Probar de nuevo en unos segundos.";
        if (cadena.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase))
            return "Falta completar un campo obligatorio.";

        return ex switch
        {
            ArgumentException or FormatException or InvalidOperationException => ex.Message,
            NotSupportedException => ex.Message,
            UnauthorizedAccessException => "Sin permisos para esa operacion.",
            TimeoutException => "La operacion tardo demasiado y se corto.",
            _ => "Error interno. El detalle quedo en el log del servidor."
        };
    }

    /// <summary>Junta el mensaje de la excepcion y el de todas sus internas.</summary>
    private static string Encadenado(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            sb.Append(e.Message).Append(" | ");
        return sb.ToString();
    }
}
