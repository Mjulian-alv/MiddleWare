using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace DataPipeline.Api.Tests;

public class RunsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public RunsApiTests(ApiFactory factory) => _factory = factory;

    private static AuthenticationHeaderValue Cred() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("pull:secreto123")));

    [Fact]
    public async Task GET_runs_requiere_auth()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GET_runs_devuelve_lista_vacia_o_corridas()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = Cred();
        var resp = await cliente.GetAsync("/api/runs?max=5");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
