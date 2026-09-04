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
}
