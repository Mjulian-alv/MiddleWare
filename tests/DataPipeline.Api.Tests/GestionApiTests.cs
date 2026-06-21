using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
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

    [Fact]
    public async Task Crud_completo_de_pipeline_como_admin()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();

        var def = new DataPipeline.Core.Pipelines.PipelineDefinition
        {
            Nombre = $"p_{Guid.NewGuid():N}",
            TablaDestino = "destino",
            ModoEscritura = DataPipeline.Core.Pipelines.ModoEscritura.Reemplazar,
            Programacion = DataPipeline.Core.Pipelines.TipoProgramacion.BajoDemanda,
            FilasPorLote = 100,
            Pasos =
            {
                new DataPipeline.Core.Pipelines.StepDefinition
                {
                    Nombre = "leer",
                    Tipo = DataPipeline.Core.Pipelines.TipoPaso.Origen,
                    Conector = "SqlServerSource"
                }
            }
        };

        // POST crea
        var post = await cliente.PostAsJsonAsync("/api/pipelines", def);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var creado = await post.Content.ReadFromJsonAsync<RespId>();
        Assert.NotNull(creado);
        Assert.NotEqual(Guid.Empty, creado!.Id);

        // GET by id devuelve
        var get = await cliente.GetAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // GET lista incluye al menos 1
        var lista = await cliente.GetFromJsonAsync<List<DataPipeline.Core.Pipelines.PipelineDefinition>>("/api/pipelines");
        Assert.NotNull(lista);
        Assert.Contains(lista!, p => p.Id == creado.Id);

        // DELETE borra (204) y luego GET da 404
        var del = await cliente.DeleteAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var get2 = await cliente.GetAsync($"/api/pipelines/{creado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get2.StatusCode);
    }

    [Fact]
    public async Task Delete_inexistente_devuelve_404()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();
        var resp = await cliente.DeleteAsync($"/api/pipelines/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record RespId(Guid Id);
}
