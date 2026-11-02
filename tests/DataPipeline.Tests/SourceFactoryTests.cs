using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class SourceFactoryTests
{
    private static JsonElement S(string valor) =>
        JsonSerializer.SerializeToElement(valor);

    [Fact]
    public void Crea_un_SqlServerSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer",
            Tipo = TipoPaso.Origen,
            Conector = "SqlServerSource",
            Configuracion = new() { ["connectionString"] = S("Server=x;"), ["sql"] = S("SELECT 1") }
        };

        var source = factory.Create(step);

        Assert.IsType<SqlServerSource>(source);
    }

    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - CsvSource resuelto por SourceFactory.
    [Fact]
    public void Crea_un_CsvSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "CsvSource",
            Configuracion = new() { ["rutaCarpeta"] = S(@"C:\exports") }
        };

        var source = factory.Create(step);

        Assert.IsType<CsvSource>(source);
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - XlsxSource resuelto por SourceFactory.
    [Fact]
    public void Crea_un_XlsxSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "XlsxSource",
            Configuracion = new() { ["rutaCarpeta"] = S(@"C:\exports") }
        };

        var source = factory.Create(step);

        Assert.IsType<XlsxSource>(source);
    }

    [Fact]
    public void Falta_config_requerida_lanza()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" };

        Assert.Throws<ArgumentException>(() => factory.Create(step));
    }

    [Fact]
    public void Tipo_no_soportado_lanza()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Origen, Conector = "KafkaSource" };

        Assert.Throws<NotSupportedException>(() => factory.Create(step));
    }

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Origen DataHub CompLook.
    [Fact]
    public void Resuelve_DataHubSource()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "DataHubSource",
            Configuracion = new()
            {
                ["baseUrl"] = S("http://srv-datahub:5000"),
                ["ruta"] = S("ventas/facturas"),
                ["metodoHttp"] = S("GET"),
                ["parametros"] = JsonSerializer.SerializeToElement(
                    new Dictionary<string, string> { ["sucursal"] = "001", ["watermark"] = "" }),
                ["esquema"] = S("""[{"name":"nro","dataType":"número"}]"""),
                ["usuario"] = S("dataPipeline"),
                ["password"] = S("secreta")
            }
        };

        Assert.IsType<DataHubSource>(factory.Create(step));
    }

    [Fact]
    public void DataHubSource_sin_ruta_lanza()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "DataHubSource",
            Configuracion = new() { ["baseUrl"] = S("http://srv-datahub:5000") }
        };

        Assert.Throws<ArgumentException>(() => factory.Create(step));
    }
}
