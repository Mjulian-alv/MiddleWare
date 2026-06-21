using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataPipeline.Api.Tests;

public class DataSourcesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public DataSourcesApiTests(ApiFactory factory) => _factory = factory;

    private sealed record Upsert(Guid Id, string Nombre, int Tipo, string? ConnectionString,
        string? Username, string? Password, string? BaseUrl, string? FilePath, bool Activo);
    private sealed record Vista(Guid Id, string Nombre, int Tipo, bool Activo,
        DateTime CreatedAt, DateTime? UpdatedAt, bool TieneConnectionString);
    private sealed record RespId(Guid Id);

    [Fact]
    public async Task Pull_no_accede_403()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredPull();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/datasources")).StatusCode);
    }

    [Fact]
    public async Task Crud_y_los_secretos_no_se_devuelven()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = ApiFactory.CredAdmin();

        var body = new Upsert(Guid.Empty, $"ds_{Guid.NewGuid():N}", 0,
            "Server=secreto;Database=db;", "user", "pass", null, null, true);

        var post = await c.PostAsJsonAsync("/api/datasources", body);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var creado = await post.Content.ReadFromJsonAsync<RespId>();
        Assert.NotEqual(Guid.Empty, creado!.Id);

        // GET by id: sin secretos, pero marca que tiene connectionString
        var vista = await c.GetFromJsonAsync<Vista>($"/api/datasources/{creado.Id}");
        Assert.NotNull(vista);
        Assert.True(vista!.TieneConnectionString);

        // El cuerpo crudo NO debe contener el secreto en claro
        var raw = await (await c.GetAsync($"/api/datasources/{creado.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Server=secreto", raw);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);

        // GET lista incluye al creado
        var lista = await c.GetFromJsonAsync<List<Vista>>("/api/datasources");
        Assert.Contains(lista!, v => v.Id == creado.Id);

        // DELETE 204 y luego 404
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/datasources/{creado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/datasources/{creado.Id}")).StatusCode);
    }
}
