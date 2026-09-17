// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: CRUD y reintento.
using System.Net;
using System.Net.Http.Json;
using DataPipeline.Core.Envios;
using Xunit;

namespace DataPipeline.Api.Tests;

public class EnviosApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public EnviosApiTests(ApiFactory factory) => _factory = factory;

    private HttpClient ClienteAdmin()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        return cliente;
    }

    private static Envio Nuevo() => new()
    {
        Id = Guid.NewGuid(), Nombre = $"e_{Guid.NewGuid():N}", Habilitado = true,
        PipelineId = Guid.NewGuid(), DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
    };

    [Fact]
    public async Task Sin_credenciales_devuelve_401()
    {
        var r = await _factory.CreateClient().GetAsync("api/envios");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task El_usuario_pull_no_accede_403()
    {
        // El consumidor externo no puede crear envios: podria apuntarlos a cualquier servidor SQL
        // usando las credenciales ya guardadas en los origenes.
        var pull = _factory.CreateClient();
        pull.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();

        Assert.Equal(HttpStatusCode.Forbidden, (await pull.GetAsync("api/envios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pull.PostAsJsonAsync("api/envios", Nuevo())).StatusCode);
    }

    [Fact]
    public async Task Crea_lista_y_borra_un_envio()
    {
        var client = ClienteAdmin();
        var envio = Nuevo();

        var creado = await client.PostAsJsonAsync("api/envios", envio);
        Assert.Equal(HttpStatusCode.OK, creado.StatusCode);

        var lista = await client.GetFromJsonAsync<List<Envio>>("api/envios");
        Assert.Contains(lista!, e => e.Id == envio.Id);

        var borrado = await client.DeleteAsync($"api/envios/{envio.Id}");
        Assert.Equal(HttpStatusCode.OK, borrado.StatusCode);

        var despues = await client.GetAsync($"api/envios/{envio.Id}");
        Assert.Equal(HttpStatusCode.NotFound, despues.StatusCode);
    }

    [Theory]
    [InlineData("TablaDestino")]
    [InlineData("Nombre")]
    [InlineData("PipelineId")]
    [InlineData("DataSourceId")]
    public async Task Rechaza_un_envio_incompleto(string campoVacio)
    {
        var envio = Nuevo();
        switch (campoVacio)
        {
            case "TablaDestino": envio.TablaDestino = ""; break;
            case "Nombre": envio.Nombre = ""; break;
            case "PipelineId": envio.PipelineId = Guid.Empty; break;
            case "DataSourceId": envio.DataSourceId = Guid.Empty; break;
        }

        var r = await ClienteAdmin().PostAsJsonAsync("api/envios", envio);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Rechaza_un_nombre_de_tabla_invalido()
    {
        // Tres partes apuntarian a otra base del servidor remoto.
        var envio = Nuevo();
        envio.TablaDestino = "otra_base.dbo.mw_x";

        var r = await ClienteAdmin().PostAsJsonAsync("api/envios", envio);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Reintentar_reactiva_un_envio_fallido()
    {
        var client = ClienteAdmin();
        var envio = Nuevo();
        envio.Habilitado = false;
        envio.IntentosFallidos = Envio.MaxIntentos;
        await client.PostAsJsonAsync("api/envios", envio);

        var r = await client.PostAsync($"api/envios/{envio.Id}/reintentar", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var recargado = await client.GetFromJsonAsync<Envio>($"api/envios/{envio.Id}");
        Assert.True(recargado!.Habilitado);
        Assert.Equal(0, recargado.IntentosFallidos);
        Assert.True(recargado.Pendiente);
    }

    [Fact]
    public async Task Reintentar_un_envio_inexistente_devuelve_404()
    {
        var r = await ClienteAdmin().PostAsync($"api/envios/{Guid.NewGuid()}/reintentar", null);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
