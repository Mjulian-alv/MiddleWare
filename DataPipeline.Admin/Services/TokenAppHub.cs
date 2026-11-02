// Fecha: 02/11/2026 - TAREA: 00157 - Linea: 1 - Identidad que manda AppHub: JWT HS256 de vida corta en el encabezado X-AppHub-Identidad
// (contrato en AppHub/Docs/Contrato-Apps.md, v0.2). Sin AppHub:ClaveCompartida el Admin funciona solo, como antes.
// Es el mismo validador del piloto del Conciliador (TAREA 00140): mismos controles, en el mismo orden.
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DataPipeline.Admin.Services;

/// <summary>Configuracion de la seccion «AppHub». Con la clave vacia la identidad de AppHub esta apagada (modo autonomo).</summary>
public sealed record IdentidadAppHubOptions(string Aplicacion, string Clave, int ToleranciaSegundos)
{
    public bool Habilitada => Clave.Length > 0;

    public static IdentidadAppHubOptions Desde(IConfiguration c)
    {
        var clave = (c["AppHub:ClaveCompartida"] ?? "").Trim();
        // El contrato pide al menos 32 caracteres: una clave corta se adivina; se prefiere no arrancar a funcionar mal.
        if (clave.Length > 0 && clave.Length < 32)
            throw new InvalidOperationException("AppHub:ClaveCompartida necesita al menos 32 caracteres (o dejala vacia para el modo autonomo).");

        var app = (c["AppHub:Aplicacion"] ?? "").Trim();
        if (clave.Length > 0 && app.Length == 0)
            throw new InvalidOperationException("Configura AppHub:Aplicacion (la clave de esta app en AppHub).");

        return new(app, clave, Math.Clamp(c.GetValue("AppHub:ToleranciaSegundos", 30), 0, 300));
    }
}

/// <summary>Lo que AppHub dice de quien llega: correo (obligatorio), nombre para mostrar y oid de Microsoft si vino.</summary>
public sealed record IdentidadToken(string Correo, string Nombre, string? Oid);

public static class TokenAppHub
{
    public const string Encabezado = "X-AppHub-Identidad";

    /// <summary>Vida del token segun el contrato (exp - iat).</summary>
    public const int VidaMaximaSegundos = 60;

    private static byte[]? Base64Url(string s)
    {
        try
        {
            var t = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
        }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// Valida firma, algoritmo, emisor, destinatario, vencimiento y correo. Devuelve null y el motivo
    /// (sin datos del token) si algo falla.
    /// </summary>
    public static IdentidadToken? Validar(string token, IdentidadAppHubOptions o, DateTimeOffset ahora, out string motivo)
    {
        motivo = "";
        var partes = token.Split('.');
        if (partes.Length != 3 || partes.Any(p => p.Length == 0 || p.Length > 4096)) { motivo = "formato"; return null; }

        var cab = Base64Url(partes[0]);
        var carga = Base64Url(partes[1]);
        var firma = Base64Url(partes[2]);
        if (cab == null || carga == null || firma == null) { motivo = "base64"; return null; }

        try
        {
            using var h = JsonDocument.Parse(cab);
            if (h.RootElement.ValueKind != JsonValueKind.Object
                || !h.RootElement.TryGetProperty("alg", out var alg)
                || alg.ValueKind != JsonValueKind.String
                || alg.GetString() != "HS256") { motivo = "algoritmo"; return null; }

            var esperada = HMACSHA256.HashData(Encoding.UTF8.GetBytes(o.Clave), Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]));
            if (!CryptographicOperations.FixedTimeEquals(esperada, firma)) { motivo = "firma"; return null; }

            using var d = JsonDocument.Parse(carga);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { motivo = "carga"; return null; }

            string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            long? N(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : null;

            if (S("iss") != "AppHub") { motivo = "emisor"; return null; }
            if (!string.Equals(S("aud"), o.Aplicacion, StringComparison.Ordinal)) { motivo = "destinatario"; return null; }

            var exp = N("exp");
            var iat = N("iat");
            var t = ahora.ToUnixTimeSeconds();
            if (exp == null || exp < t - o.ToleranciaSegundos) { motivo = "vencido"; return null; }
            if (iat == null || iat > t + o.ToleranciaSegundos) { motivo = "emitido en el futuro"; return null; }
            // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 1 - Revision 1: se acota la vida del token. El contrato dice 60 s; sin este tope un token con
            // exp lejano (bug de AppHub, o uno filtrado de un log del proxy) abriria sesion de administrador todo ese tiempo.
            if (exp <= iat || exp - iat > VidaMaximaSegundos + o.ToleranciaSegundos) { motivo = "vida invalida"; return null; }

            var correo = (S("email") ?? "").Trim();
            // Fecha: 02/11/2026 - TAREA: 00157 - Linea: 1 - Revision 1: el correo va al log; sin caracteres de control.
            if (correo.Length == 0 || correo.Length > 200 || correo.Any(char.IsControl) || !MailAddress.TryCreate(correo, out var m) || m.Address != correo || !correo.Contains('@'))
            { motivo = "correo"; return null; }

            var nombre = (S("name") ?? "").Trim();
            return new(correo,
                nombre.Length == 0 ? correo : (nombre.Length > 200 ? nombre[..200] : nombre),
                string.IsNullOrWhiteSpace(S("sub")) ? null : S("sub"));
        }
        catch (JsonException) { motivo = "json"; return null; }
    }
}
