namespace DataPipeline.Admin.Services;

/// <summary>
/// Estado de sesion del Admin en memoria, scoped por circuito Blazor Server.
/// (Una recarga completa de la pagina reinicia el circuito y exige volver a ingresar.)
/// </summary>
public class AdminSessionState
{
    public bool IsAuthenticated { get; private set; }
    public string? Username { get; private set; }

    public event Action? OnChange;

    public void Autenticar(string username)
    {
        IsAuthenticated = true;
        Username = username;
        OnChange?.Invoke();
    }

    public void CerrarSesion()
    {
        IsAuthenticated = false;
        Username = null;
        OnChange?.Invoke();
    }
}
