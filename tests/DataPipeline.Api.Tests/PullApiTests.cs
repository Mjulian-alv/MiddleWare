using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace DataPipeline.Api.Tests;

public class PullApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PullApiTests(ApiFactory factory) => _factory = factory;

    private static AuthenticationHeaderValue Cred(string u, string p) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{u}:{p}")));

    [Fact]
    public async Task Sin_credenciales_devuelve_401()
    {
        var cliente = _factory.CreateClient();
        var resp = await cliente.GetAsync("/api/data/ventas");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Con_credenciales_devuelve_las_filas_de_la_tabla()
    {
        await _factory.SeedPipelineConDatosAsync("ventas", "serving_ventas");
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred("pull", "secreto123");

        var resp = await cliente.GetAsync("/api/data/ventas?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"rows\"", json);
        Assert.Contains("importe", json);
    }

    [Fact]
    public async Task Pipeline_inexistente_devuelve_404()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred("pull", "secreto123");
        var resp = await cliente.GetAsync("/api/data/noexiste");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
