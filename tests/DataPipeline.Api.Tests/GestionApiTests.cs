using System.Net;
using Xunit;

namespace DataPipeline.Api.Tests;

public class GestionApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public GestionApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Sin_credenciales_devuelve_401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Pull_no_accede_a_gestion_403()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();
        var resp = await cliente.GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_accede_a_gestion_200()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        var resp = await cliente.GetAsync("/api/pipelines");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
