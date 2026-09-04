// Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1
using DataPipeline.Connectors.Testing;
using DataPipeline.Core.DataSources;
using Xunit;

namespace DataPipeline.Tests;

public class SchemaDiscovererArchivoTests
{
    [Fact]
    public async Task Descubre_columnas_del_csv_mas_nuevo_sin_procesar()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"disc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id,importe\n1,100\n");
        var ds = new DataSource { Tipo = TipoDataSource.Csv, RutaCarpeta = carpeta, Separador = ",", TieneEncabezado = true };
        var discoverer = new SqlServerSchemaDiscoverer();

        var r = await discoverer.DescubrirArchivoAsync(ds, CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(new[] { "id", "importe" }, r.Columnas!.Select(c => c.Nombre));
    }

    [Fact]
    public async Task Sin_archivos_da_Ok_false_con_mensaje()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"disc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        var ds = new DataSource { Tipo = TipoDataSource.Csv, RutaCarpeta = carpeta };
        var discoverer = new SqlServerSchemaDiscoverer();

        var r = await discoverer.DescubrirArchivoAsync(ds, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }
}
