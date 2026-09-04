// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1
using DataPipeline.Connectors.Testing;
using Xunit;

namespace DataPipeline.Tests;

public class ConnectionTesterArchivoTests
{
    [Fact]
    public async Task Carpeta_inexistente_da_Ok_false()
    {
        var tester = new ConnectionTester();
        var r = await tester.ProbarArchivoAsync(@"C:\no-existe-" + Guid.NewGuid(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public async Task Carpeta_existente_y_escribible_da_Ok_true()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"probar_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        var tester = new ConnectionTester();

        var r = await tester.ProbarArchivoAsync(carpeta, CancellationToken.None);

        Assert.True(r.Ok);
    }
}
