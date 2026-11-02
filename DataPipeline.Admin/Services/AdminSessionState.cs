using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace DataPipeline.Admin.Services;

// Fecha: 02/11/2026 - TAREA: 00157 - Linea: 3 - La sesion recuerda si vino de AppHub: de ahi se sale desde AppHub, no desde el Admin.
/// <summary>De donde vino la sesion: del login propio (dpAdmin) o de AppHub.</summary>
public enum OrigenSesion { Local, AppHub }

/// <summary>
/// Estado de sesion del Admin en memoria, scoped por circuito Blazor Server.
/// (Una recarga completa de la pagina reinicia el circuito y exige volver a ingresar.)
/// </summary>
public class AdminSessionState(AuthenticationStateProvider auth, ILogger<AdminSessionState> log)
{
    private Task? _inicializacion;

    public bool IsAuthenticated { get; private set; }
    public string? Username { get; private set; }

    // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 3 - Origen de la sesion y aviso de una identidad de AppHub rechazada.
    public OrigenSesion Origen { get; private set; } = OrigenSesion.Local;

    /// <summary>True si el pedido traia una identidad de AppHub que no se pudo confirmar (el login lo explica).</summary>
    public bool AppHubRechazada { get; private set; }

    public event Action? OnChange;

    public void Autenticar(string username)
    {
        IsAuthenticated = true;
        Username = username;
        // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 3 - El login propio siempre deja una sesion local (con «Salir»).
        Origen = OrigenSesion.Local;
        OnChange?.Invoke();
    }

    // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 2,3 - Toma la identidad que dejo el middleware de AppHub en el usuario del circuito.
    // Quien AppHub deja abrir entra como administrador (el Admin es todo-o-nada; no hay usuarios ni permisos propios).
    // Se lee una sola vez por circuito, y nunca pisa una sesion ya abierta con el login propio.
    // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 3 - Revision 1: se guarda la Task y no un booleano. Con el booleano un segundo llamador volvia de
    // inmediato con IsAuthenticated en false mientras el primero seguia esperando, y si la lectura fallaba la identidad no se volvia a leer.
    public Task InicializarAsync() => _inicializacion ??= LeerIdentidadAsync();

    private async Task LeerIdentidadAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        AppHubRechazada = user.HasClaim(c => c.Type == IdentidadAppHub.ClaimRechazada);
        if (IsAuthenticated || !user.HasClaim(c => c.Type == IdentidadAppHub.ClaimOrigen)) return;

        var correo = user.FindFirstValue(IdentidadAppHub.ClaimCorreo);
        if (string.IsNullOrWhiteSpace(correo)) return;

        IsAuthenticated = true;
        Username = correo;
        Origen = OrigenSesion.AppHub;
        log.LogInformation("Ingreso por AppHub: {Correo} (administrador).", correo);
        OnChange?.Invoke();
    }

    public void CerrarSesion()
    {
        IsAuthenticated = false;
        Username = null;
        OnChange?.Invoke();
    }
}
