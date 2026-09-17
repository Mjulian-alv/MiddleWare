// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el hosted service resuelve
// EjecutorEnvios DENTRO del tick y traga la excepcion logueandola. Un DI mal armado no romperia el
// arranque ni ningun otro test: en produccion seria un error en el log cada 60s y ningun envio saldria.
using DataPipeline.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataPipeline.Api.Tests;

public class EnvioDiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public EnvioDiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void Resuelve_los_dos_ejecutores_que_usa_el_tick()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EjecutorProgramado>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EjecutorEnvios>());
    }
}
