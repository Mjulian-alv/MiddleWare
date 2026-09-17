// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: politica de reintentos.
// Pura, separada del ejecutor, porque es la regla que mas se va a discutir y la que mas barato es
// cambiar si se testea sola.
using DataPipeline.Core.Envios;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class BackoffEnviosTests
{
    private static readonly DateTime Ahora = new(2026, 9, 12, 3, 4, 0);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void Espacia_los_reintentos_1_2_4_8(int previos, int minutos)
    {
        var (proximo, deshabilitar) = BackoffEnvios.Calcular(previos, Ahora);

        Assert.Equal(Ahora.AddMinutes(minutos), proximo);
        Assert.False(deshabilitar);
    }

    [Fact]
    public void El_quinto_fallo_agota_el_tope_y_deshabilita()
    {
        // MaxIntentos = 5 fallos TOTALES. Con 4 previos, este es el 5o y ultimo: ya no reprograma.
        var (proximo, deshabilitar) = BackoffEnvios.Calcular(Envio.MaxIntentos - 1, Ahora);

        Assert.Null(proximo);
        Assert.True(deshabilitar);
    }

    [Fact]
    public void El_cuarto_fallo_todavia_reprograma()
    {
        Assert.NotNull(BackoffEnvios.Calcular(3, Ahora).ProximoIntento);
        Assert.False(BackoffEnvios.Calcular(3, Ahora).Deshabilitar);
    }
}
