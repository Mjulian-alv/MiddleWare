using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using Xunit;

namespace DataPipeline.Tests;

public class ScheduleCalculatorTests
{
    private static PipelineDefinition Def(TipoProgramacion prog, string hora) =>
        new() { Programacion = prog, HoraDelDia = hora };

    [Fact]
    public void BajoDemanda_devuelve_null()
    {
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.BajoDemanda, "08:00"), new DateTime(2026, 6, 20, 9, 0, 0));
        Assert.Null(r);
    }

    [Fact]
    public void Diaria_antes_de_la_hora_es_hoy()
    {
        var desde = new DateTime(2026, 6, 20, 6, 0, 0);
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.Diaria, "08:00"), desde);
        Assert.Equal(new DateTime(2026, 6, 20, 8, 0, 0), r);
    }

    [Fact]
    public void Diaria_despues_de_la_hora_es_manana()
    {
        var desde = new DateTime(2026, 6, 20, 9, 0, 0);
        var r = ScheduleCalculator.CalcularProxima(Def(TipoProgramacion.Diaria, "08:00"), desde);
        Assert.Equal(new DateTime(2026, 6, 21, 8, 0, 0), r);
    }

    [Fact]
    public void Mensual_usa_dia_del_mes()
    {
        var def = new PipelineDefinition { Programacion = TipoProgramacion.Mensual, HoraDelDia = "02:00", DiaDelMes = 5 };
        var r = ScheduleCalculator.CalcularProxima(def, new DateTime(2026, 6, 20, 9, 0, 0));
        Assert.Equal(new DateTime(2026, 7, 5, 2, 0, 0), r);
    }
}
