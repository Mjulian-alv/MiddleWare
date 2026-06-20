using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class SourceFactoryTests
{
    [Fact]
    public void Crea_un_SqlServerSource_para_el_tipo_correcto()
    {
        var factory = new SourceFactory();
        var step = new StepDefinition
        {
            Nombre = "leer",
            Tipo = TipoPaso.Origen,
            Conector = "SqlServerSource",
            Configuracion = new() { ["connectionString"] = "Server=x;", ["sql"] = "SELECT 1" }
        };

        var source = factory.Create(step);

        Assert.IsType<SqlServerSource>(source);
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
