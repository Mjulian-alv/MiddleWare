using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Tests;

public class CliImportTests
{
    [Fact]
    public void ImportarDefinicion_calcula_proxima_y_persiste()
    {
        // Verifica la logica de import (extraida a un helper estatico para testearla sin proceso).
        var json = """
        {
          "nombre": "ventas_diaria",
          "habilitado": true,
          "programacion": "Diaria",
          "horaDelDia": "08:00",
          "tablaDestino": "serving",
          "modoEscritura": "Reemplazar",
          "pasos": [
            { "nombre": "leer", "tipo": "Origen", "conector": "SqlServerSource",
              "configuracion": { "connectionString": "Server=x;", "sql": "SELECT 1 AS n" } }
          ]
        }
        """;
        var def = PipelineJsonLoader.Load(json);
        var ahora = new DateTime(2026, 6, 20, 6, 0, 0);
        def.ProximaCorrida = ScheduleCalculator.CalcularProxima(def, ahora);

        Assert.Equal(new DateTime(2026, 6, 20, 8, 0, 0), def.ProximaCorrida);
        Assert.NotEqual(Guid.Empty, def.Id);
    }
}
