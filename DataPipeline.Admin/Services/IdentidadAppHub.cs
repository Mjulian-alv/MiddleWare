// Fecha: 02/11/2026 - TAREA: 00157 - Linea: 2,3 - Si el pedido trae una identidad valida de AppHub, la deja en HttpContext.User;
// sin clave configurada no hace nada.
// El Admin no tiene cookie: su sesion vive en el circuito de Blazor. Blazor toma el usuario del pedido que crea el circuito
// (la negociacion de /_blazor y el websocket, que tambien pasan por aca), asi que dejar la identidad en HttpContext.User es lo que
// la hace llegar al circuito. Van como claims de un principal SIN tipo de autenticacion: un principal asi no esta autenticado
// para ASP.NET (no abre nada por si solo); solo AdminSessionState lo lee, y es quien decide.
using System.Security.Claims;

namespace DataPipeline.Admin.Services;

public sealed class IdentidadAppHub(RequestDelegate next, IdentidadAppHubOptions o, ILogger<IdentidadAppHub> log)
{
    public const string ClaimOrigen = "apphub";
    public const string ClaimCorreo = "apphub:correo";
    public const string ClaimNombre = "apphub:nombre";
    /// <summary>Marca que el pedido traia una identidad que no se pudo confirmar: el login explica por que no se entro por AppHub.</summary>
    public const string ClaimRechazada = "apphub:rechazada";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!o.Habilitada || !ctx.Request.Headers.TryGetValue(TokenAppHub.Encabezado, out var valores))
        {
            await next(ctx);
            return;
        }

        var motivo = "encabezado repetido";
        IdentidadToken? id = null;
        if (valores.Count == 1)
            id = TokenAppHub.Validar(valores[0] ?? "", o, DateTimeOffset.UtcNow, out motivo);

        // Nunca se loguea el contenido del token, solo el motivo.
        if (id == null)
        {
            log.LogWarning("Identidad de AppHub rechazada: {Motivo}.", motivo);
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimRechazada, "1")]));
        }
        else
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimOrigen, "1"),
                new Claim(ClaimCorreo, id.Correo),
                new Claim(ClaimNombre, id.Nombre)]));
        }

        await next(ctx);
    }
}
