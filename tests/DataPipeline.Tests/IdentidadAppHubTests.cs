// Fecha: 02/11/2026 - TAREA: 00157 - Linea: 1,2,3 - Identidad de AppHub en el Admin.
// Cubre el checklist del contrato (Docs/Contrato-Apps.md, seccion 7) en lo que le toca al Admin: lo que mas importa es lo que
// NO abre sesion (token vencido, firma mala, alg=none, otra app, sin correo, encabezado repetido), no el camino feliz.
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataPipeline.Admin.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataPipeline.Tests;

public class IdentidadAppHubTests
{
    private const string Clave = "0123456789abcdef0123456789abcdef-clave-de-prueba";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly IdentidadAppHubOptions Opciones = new("datapipeline", Clave, 30);

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string B64(string s) => B64(Encoding.UTF8.GetBytes(s));

    private static string Token(Dictionary<string, object?>? carga = null, string alg = "HS256", string clave = Clave, Action<Dictionary<string, object?>>? ajustar = null)
    {
        var c = carga ?? new Dictionary<string, object?>
        {
            ["iss"] = "AppHub",
            ["aud"] = "datapipeline",
            ["sub"] = "oid-123",
            ["email"] = "ana@alvearsupermercados.com.ar",
            ["name"] = "Ana Perez",
            ["iat"] = Ahora.ToUnixTimeSeconds(),
            ["exp"] = Ahora.ToUnixTimeSeconds() + 60,
            ["jti"] = "t1",
        };
        ajustar?.Invoke(c);
        var cab = B64(JsonSerializer.Serialize(new { alg, typ = "JWT" }));
        var cuerpo = B64(JsonSerializer.Serialize(c));
        var firma = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(clave), Encoding.ASCII.GetBytes(cab + "." + cuerpo)));
        return $"{cab}.{cuerpo}.{firma}";
    }

    // ── Validador ────────────────────────────────────────────────────────────

    [Fact]
    public void Token_correcto_devuelve_correo_nombre_y_oid()
    {
        var id = TokenAppHub.Validar(Token(), Opciones, Ahora, out var motivo);
        Assert.NotNull(id);
        Assert.Equal("ana@alvearsupermercados.com.ar", id.Correo);
        Assert.Equal("Ana Perez", id.Nombre);
        Assert.Equal("oid-123", id.Oid);
        Assert.Equal("", motivo);
    }

    [Fact]
    public void Sin_nombre_usa_el_correo_y_sin_sub_no_hay_oid()
    {
        var t = Token(ajustar: c => { c.Remove("name"); c.Remove("sub"); });
        var id = TokenAppHub.Validar(t, Opciones, Ahora, out _);
        Assert.NotNull(id);
        Assert.Equal(id.Correo, id.Nombre);
        Assert.Null(id.Oid);
    }

    [Theory]
    [InlineData("vencido")]
    [InlineData("firma")]
    [InlineData("alg-none")]
    [InlineData("alg-hs512")]
    [InlineData("otra-app")]
    [InlineData("otro-emisor")]
    [InlineData("sin-correo")]
    [InlineData("correo-malo")]
    [InlineData("futuro")]
    [InlineData("sin-exp")]
    public void Lo_que_no_cumple_el_contrato_se_rechaza(string caso)
    {
        var t = caso switch
        {
            "vencido" => Token(ajustar: c => c["exp"] = Ahora.ToUnixTimeSeconds() - 31),
            "firma" => Token(clave: "otra-clave-otra-clave-otra-clave-otra-clave"),
            "alg-none" => Token(alg: "none"),
            "alg-hs512" => Token(alg: "HS512"),
            "otra-app" => Token(ajustar: c => c["aud"] = "conciliador"),
            "otro-emisor" => Token(ajustar: c => c["iss"] = "Otro"),
            "sin-correo" => Token(ajustar: c => c.Remove("email")),
            "correo-malo" => Token(ajustar: c => c["email"] = "no es un correo"),
            "futuro" => Token(ajustar: c => c["iat"] = Ahora.ToUnixTimeSeconds() + 31),
            "sin-exp" => Token(ajustar: c => c.Remove("exp")),
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
        Assert.Null(TokenAppHub.Validar(t, Opciones, Ahora, out var motivo));
        Assert.NotEqual("", motivo);
    }

    [Fact]
    public void Dentro_de_la_tolerancia_todavia_vale()
    {
        var t = Token(ajustar: c => c["exp"] = Ahora.ToUnixTimeSeconds() - 30);
        Assert.NotNull(TokenAppHub.Validar(t, Opciones, Ahora, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("..")]
    [InlineData("!!!.???.***")]
    public void Basura_se_rechaza_sin_tirar(string token)
    {
        Assert.Null(TokenAppHub.Validar(token, Opciones, Ahora, out var motivo));
        Assert.NotEqual("", motivo);
    }

    [Fact]
    public void El_motivo_no_incluye_datos_del_token()
    {
        var t = Token(ajustar: c => c["aud"] = "conciliador");
        TokenAppHub.Validar(t, Opciones, Ahora, out var motivo);
        Assert.DoesNotContain("ana@", motivo);
        Assert.DoesNotContain(Clave, motivo);
    }

    // ── Configuracion ────────────────────────────────────────────────────────

    private static IConfiguration Config(params (string, string?)[] v) =>
        new ConfigurationBuilder().AddInMemoryCollection(v.Select(x => new KeyValuePair<string, string?>(x.Item1, x.Item2))).Build();

    [Fact]
    public void Sin_clave_queda_apagada()
        => Assert.False(IdentidadAppHubOptions.Desde(Config(("AppHub:Aplicacion", "datapipeline"), ("AppHub:ClaveCompartida", ""))).Habilitada);

    [Fact]
    public void Clave_corta_no_arranca()
        => Assert.Throws<InvalidOperationException>(() => IdentidadAppHubOptions.Desde(Config(("AppHub:Aplicacion", "datapipeline"), ("AppHub:ClaveCompartida", "corta"))));

    [Fact]
    public void Clave_sin_aplicacion_no_arranca()
        => Assert.Throws<InvalidOperationException>(() => IdentidadAppHubOptions.Desde(Config(("AppHub:ClaveCompartida", Clave))));

    [Fact]
    public void Tolerancia_se_acota()
    {
        var o = IdentidadAppHubOptions.Desde(Config(("AppHub:Aplicacion", "datapipeline"), ("AppHub:ClaveCompartida", Clave), ("AppHub:ToleranciaSegundos", "9999")));
        Assert.Equal(300, o.ToleranciaSegundos);
    }

    // ── Middleware + sesion ──────────────────────────────────────────────────

    private static async Task<ClaimsPrincipal> Pasar(IdentidadAppHubOptions o, params string[] encabezados)
    {
        var ctx = new DefaultHttpContext();
        if (encabezados.Length > 0) ctx.Request.Headers[TokenAppHub.Encabezado] = encabezados;
        var llamado = false;
        await new IdentidadAppHub(_ => { llamado = true; return Task.CompletedTask; }, o, NullLogger<IdentidadAppHub>.Instance).InvokeAsync(ctx);
        Assert.True(llamado, "El middleware siempre tiene que seguir con el pedido.");
        return ctx.User;
    }

    private sealed class Proveedor(ClaimsPrincipal u) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(u));
    }

    private static AdminSessionState Sesion(ClaimsPrincipal u) => new(new Proveedor(u), NullLogger<AdminSessionState>.Instance);

    // El token se arma con el reloj real: el middleware usa UtcNow.
    private static string TokenAhora(Action<Dictionary<string, object?>>? ajustar = null)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Token(new Dictionary<string, object?>
        {
            ["iss"] = "AppHub", ["aud"] = "datapipeline", ["sub"] = "oid-123",
            ["email"] = "ana@alvearsupermercados.com.ar", ["name"] = "Ana Perez",
            ["iat"] = t, ["exp"] = t + 60, ["jti"] = "t1",
        }, ajustar: ajustar);
    }

    [Fact]
    public async Task Token_valido_abre_sesion_de_administrador_desde_AppHub()
    {
        var s = Sesion(await Pasar(Opciones, TokenAhora()));
        await s.InicializarAsync();
        Assert.True(s.IsAuthenticated);
        Assert.Equal("ana@alvearsupermercados.com.ar", s.Username);
        Assert.Equal(OrigenSesion.AppHub, s.Origen);
        Assert.False(s.AppHubRechazada);
    }

    [Fact]
    public async Task Sin_clave_configurada_el_encabezado_se_ignora()
    {
        var s = Sesion(await Pasar(Opciones with { Clave = "" }, TokenAhora()));
        await s.InicializarAsync();
        Assert.False(s.IsAuthenticated);
        Assert.False(s.AppHubRechazada);
    }

    [Fact]
    public async Task Sin_encabezado_no_hay_sesion_ni_aviso()
    {
        var s = Sesion(await Pasar(Opciones));
        await s.InicializarAsync();
        Assert.False(s.IsAuthenticated);
        Assert.False(s.AppHubRechazada);
    }

    [Theory]
    [InlineData("vencido")]
    [InlineData("firma")]
    [InlineData("alg-none")]
    [InlineData("otra-app")]
    [InlineData("sin-correo")]
    [InlineData("basura")]
    public async Task Token_malo_no_abre_sesion_y_deja_el_aviso(string caso)
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var t = caso switch
        {
            "vencido" => TokenAhora(c => c["exp"] = ahora - 120),
            "firma" => Token(clave: "otra-clave-otra-clave-otra-clave-otra-clave"),
            "alg-none" => Token(alg: "none"),
            "otra-app" => TokenAhora(c => c["aud"] = "conciliador"),
            "sin-correo" => TokenAhora(c => c.Remove("email")),
            _ => "esto.no.es-un-token",
        };
        var s = Sesion(await Pasar(Opciones, t));
        await s.InicializarAsync();
        Assert.False(s.IsAuthenticated);
        Assert.True(s.AppHubRechazada);
    }

    [Fact]
    public async Task Encabezado_repetido_se_rechaza()
    {
        var s = Sesion(await Pasar(Opciones, TokenAhora(), TokenAhora()));
        await s.InicializarAsync();
        Assert.False(s.IsAuthenticated);
        Assert.True(s.AppHubRechazada);
    }

    [Fact]
    public async Task Claims_que_el_navegador_no_puede_fabricar_no_abren_sesion()
    {
        // Sin pasar por el middleware nadie pone claims en el usuario: un principal vacio no es sesion.
        var s = Sesion(new ClaimsPrincipal(new ClaimsIdentity()));
        await s.InicializarAsync();
        Assert.False(s.IsAuthenticated);
    }

    [Fact]
    public async Task La_sesion_de_AppHub_no_pisa_una_sesion_local_ya_abierta()
    {
        var s = Sesion(await Pasar(Opciones, TokenAhora()));
        s.Autenticar("dpAdmin");
        await s.InicializarAsync();
        Assert.Equal("dpAdmin", s.Username);
        Assert.Equal(OrigenSesion.Local, s.Origen);
    }

    [Fact]
    public void Login_propio_deja_sesion_local_con_Salir()
    {
        var s = Sesion(new ClaimsPrincipal(new ClaimsIdentity()));
        s.Autenticar("dpAdmin");
        Assert.Equal(OrigenSesion.Local, s.Origen);
    }
}
