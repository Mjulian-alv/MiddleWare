using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataProcesadoApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DataProcesadoApiTests(ApiFactory factory) => _factory = factory;

    private sealed record Pagina(List<string> Columns, List<Dictionary<string, object>> Rows);
    private sealed record AckResp(int Marcadas);

    [Fact]
    public async Task Procesar_marca_y_desaparece_del_get_por_defecto()
    {
        await _factory.SeedPipelineConDatosAsync("ventasp", "serving_ventasp");
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();

        // GET inicial: 1 fila no procesada; tomamos su _key
        var pagina = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?page=1&pageSize=10");
        Assert.NotNull(pagina);
        Assert.Single(pagina!.Rows);
        var key = pagina.Rows[0]["_key"].ToString()!;

        // Ack
        var ack = await c.PostAsJsonAsync("/api/data/ventasp/procesar", new { keys = new[] { key } });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        Assert.Equal(1, (await ack.Content.ReadFromJsonAsync<AckResp>())!.Marcadas);

        // GET por defecto: ya no aparece
        var vacia = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?page=1&pageSize=10");
        Assert.Empty(vacia!.Rows);

        // GET incluirProcesados: vuelve a aparecer
        var todas = await c.GetFromJsonAsync<Pagina>("/api/data/ventasp?incluirProcesados=true");
        Assert.Single(todas!.Rows);
    }

    [Fact]
    public async Task Procesar_sin_auth_401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/data/x/procesar", new { keys = new[] { "k" } });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
