using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DataPipeline.Api.Auth;

public sealed class BasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    // 'new' explicito: oculta intencionalmente la propiedad Scheme (AuthenticationScheme) de la base;
    // aca Scheme es el nombre del esquema ("Basic") que referencian controllers y Program.
    public new const string Scheme = "Basic";
    private readonly IConfiguration _config;

    public BasicAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration config) : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var raw))
            return Task.FromResult(AuthenticateResult.Fail("Falta Authorization."));

        try
        {
            var header = AuthenticationHeaderValue.Parse(raw!);
            if (!Scheme.Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
                return Task.FromResult(AuthenticateResult.Fail("Esquema invalido."));

            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            if (partes.Length != 2) return Task.FromResult(AuthenticateResult.Fail("Credencial mal formada."));

            var userOk = _config["PullAuth:Username"];
            var hash = _config["PullAuth:PasswordHash"];
            if (userOk is null || hash is null ||
                partes[0] != userOk || !BCrypt.Net.BCrypt.Verify(partes[1], hash))
                return Task.FromResult(AuthenticateResult.Fail("Credenciales invalidas."));

            var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, partes[0]) }, Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(id), Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Error de parseo."));
        }
    }
}
