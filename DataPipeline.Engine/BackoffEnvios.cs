// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: politica de reintentos.
// Sin tope, un remoto caido tres dias genera ~4300 intentos y 4300 filas de historial. Con tope, el
// envio se apaga solo y pide intervencion desde el Admin.
using DataPipeline.Core.Envios;

namespace DataPipeline.Engine;

public static class BackoffEnvios
{
    private static readonly int[] Minutos = { 1, 2, 4, 8 };

    /// <summary>
    /// Dado cuantos fallos habia ANTES de este, decide cuando reintentar. Null + true = se agoto el
    /// tope: el envio se deshabilita y no se reintenta solo.
    ///
    /// MaxIntentos son los fallos TOTALES, no los reintentos posteriores al primero: con 4 previos
    /// este es el quinto y ultimo, asi que ya no reprograma. Por eso son cuatro esperas (1, 2, 4 y 8)
    /// y no cinco: despues de la cuarta espera viene el fallo que agota el tope.
    /// </summary>
    public static (DateTime? ProximoIntento, bool Deshabilitar) Calcular(int intentosFallidosPrevios, DateTime ahora)
    {
        if (intentosFallidosPrevios + 1 >= Envio.MaxIntentos) return (null, true);
        var minutos = Minutos[Math.Clamp(intentosFallidosPrevios, 0, Minutos.Length - 1)];
        return (ahora.AddMinutes(minutos), false);
    }
}
