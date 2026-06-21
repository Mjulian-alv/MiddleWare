using System.Text.Json;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Pipelines;
using Xunit;

namespace DataPipeline.Tests;

public class TransformFactoryTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Crea_un_ColumnMapTransform()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition
        {
            Nombre = "norm",
            Tipo = TipoPaso.Transformacion,
            Conector = "ColumnMapTransform",
            Configuracion = new()
            {
                ["rename"] = J("{\"monto\":\"importe\"}"),
                ["filter"] = J("\"importe > 0\"")
            }
        };

        var t = factory.Create(step);

        Assert.IsType<ColumnMapTransform>(t);
    }

    [Fact]
    public void Paso_que_no_es_transformacion_lanza()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Origen, Conector = "ColumnMapTransform" };
        Assert.Throws<ArgumentException>(() => factory.Create(step));
    }

    [Fact]
    public void Conector_desconocido_lanza()
    {
        var factory = new TransformFactory();
        var step = new StepDefinition { Nombre = "x", Tipo = TipoPaso.Transformacion, Conector = "Otro" };
        Assert.Throws<NotSupportedException>(() => factory.Create(step));
    }
}
