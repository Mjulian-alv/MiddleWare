using DataPipeline.Core.Pipelines;

namespace DataPipeline.Engine;

/// <summary>Calcula la proxima corrida de un pipeline (hora local del servidor).</summary>
public static class ScheduleCalculator
{
    public static DateTime? CalcularProxima(PipelineDefinition def, DateTime desde)
    {
        var hora = ParseHora(def.HoraDelDia);
        return def.Programacion switch
        {
            TipoProgramacion.Diaria => ProximaDiaria(desde, hora),
            TipoProgramacion.Semanal => ProximaSemanal(desde, hora, def.DiaDeLaSemana ?? (int)desde.DayOfWeek),
            TipoProgramacion.Mensual => ProximaMensual(desde, hora, def.DiaDelMes ?? 1),
            _ => null // BajoDemanda
        };
    }

    private static TimeSpan ParseHora(string? hhmm)
    {
        if (TimeSpan.TryParse(hhmm, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
            return ts;
        return new TimeSpan(8, 0, 0);
    }

    private static DateTime ProximaDiaria(DateTime desde, TimeSpan hora)
    {
        var candidato = desde.Date + hora;
        return candidato > desde ? candidato : candidato.AddDays(1);
    }

    private static DateTime ProximaSemanal(DateTime desde, TimeSpan hora, int diaSemana)
    {
        diaSemana = Math.Clamp(diaSemana, 0, 6);
        for (int i = 0; i < 8; i++)
        {
            var fecha = desde.Date.AddDays(i);
            if ((int)fecha.DayOfWeek == diaSemana)
            {
                var candidato = fecha + hora;
                if (candidato > desde) return candidato;
            }
        }
        return desde.Date.AddDays(7) + hora;
    }

    private static DateTime ProximaMensual(DateTime desde, TimeSpan hora, int diaMes)
    {
        diaMes = Math.Clamp(diaMes, 1, 28);
        var candidato = new DateTime(desde.Year, desde.Month, diaMes) + hora;
        if (candidato <= desde)
            candidato = new DateTime(desde.Year, desde.Month, diaMes).AddMonths(1) + hora;
        return candidato;
    }
}
