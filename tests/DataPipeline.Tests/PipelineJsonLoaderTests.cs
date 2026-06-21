using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class PipelineJsonLoaderTests
{
    private const string Json = """
    {
      "nombre": "ventas_a_serving",
      "habilitado": true,
      "programacion": "Diaria",
      "horaDelDia": "02:30",
      "filasPorLote": 5000,
      "tablaDestino": "serving_ventas",
      "modoEscritura": "Reemplazar",
      "correrDespuesDe": [],
      "pasos": [
        {
          "nombre": "leer_ventas",
          "tipo": "Origen",
          "conector": "SqlServerSource",
          "configuracion": { "connectionString": "Server=x;", "sql": "SELECT 1 AS n" }
        }
      ]
    }
    """;

    [Fact]
    public void Parsea_los_campos_principales()
    {
        var def = PipelineJsonLoader.Load(Json);

        Assert.Equal("ventas_a_serving", def.Nombre);
        Assert.Equal(TipoProgramacion.Diaria, def.Programacion);
        Assert.Equal("serving_ventas", def.TablaDestino);
        Assert.Equal(ModoEscritura.Reemplazar, def.ModoEscritura);
        Assert.Equal(5000, def.FilasPorLote);
        Assert.Single(def.Pasos);
        Assert.Equal(TipoPaso.Origen, def.Pasos[0].Tipo);
        Assert.Equal("SqlServerSource", def.Pasos[0].Conector);
        Assert.Equal("SELECT 1 AS n", def.Pasos[0].Configuracion["sql"].GetString());
    }

    [Fact]
    public void Genera_Id_cuando_no_viene_en_el_JSON()
    {
        var def = PipelineJsonLoader.Load(Json);
        Assert.NotEqual(Guid.Empty, def.Id);
    }
}
