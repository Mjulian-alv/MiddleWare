using System.Text.Json;

namespace DataPipeline.Admin.Services;

/// <summary>
/// Autenticacion local del panel Admin con un unico usuario hardcodeado (dpAdmin).
/// La contrasena se define en la primera corrida y se persiste hasheada (BCrypt) en un archivo
/// local del servidor. No depende de la API.
/// </summary>
public class AdminAuthService
{
    /// <summary>Unico usuario habilitado para el Admin.</summary>
    public const string UsuarioAdmin = "dpAdmin";

    /// <summary>Contrasena sugerida por defecto en la pantalla de configuracion inicial.</summary>
    public const string PasswordPorDefecto = "951753";

    private readonly string _rutaArchivo;
    private readonly object _candado = new();
    private string? _passwordHash;

    public AdminAuthService(IWebHostEnvironment env)
    {
        _rutaArchivo = Path.Combine(env.ContentRootPath, "admin-credentials.json");
        CargarHash();
    }

    /// <summary>True si ya se configuro la contrasena (no es la primera corrida).</summary>
    public bool EstaConfigurada => !string.IsNullOrWhiteSpace(_passwordHash);

    /// <summary>Valida usuario + contrasena contra el hash guardado.</summary>
    public bool Validar(string usuario, string password)
    {
        if (!string.Equals(usuario, UsuarioAdmin, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(_passwordHash))
            return false;
        try { return BCrypt.Net.BCrypt.Verify(password, _passwordHash); }
        catch { return false; }
    }

    /// <summary>Define (o redefine) la contrasena del admin y la persiste hasheada.</summary>
    public void EstablecerPassword(string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        lock (_candado)
        {
            var json = JsonSerializer.Serialize(new Credenciales { PasswordHash = hash });
            File.WriteAllText(_rutaArchivo, json);
            _passwordHash = hash;
        }
    }

    private void CargarHash()
    {
        try
        {
            if (!File.Exists(_rutaArchivo)) return;
            var cred = JsonSerializer.Deserialize<Credenciales>(File.ReadAllText(_rutaArchivo));
            _passwordHash = cred?.PasswordHash;
        }
        catch { _passwordHash = null; }
    }

    private sealed class Credenciales
    {
        public string? PasswordHash { get; set; }
    }
}
