using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Procesa los pipelines vencidos: corre, registra historial y recalcula la proxima corrida.</summary>
public sealed class EjecutorProgramado
{
    private readonly IPipelineStore _store;
    private readonly IHistorialCorridas _historial;
    private readonly PipelineRunner _runner;
    private readonly IEncryptionService _encryption;
    private readonly PipelineHydrator _hydrator;
    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push.
    private readonly IEnvioStore _envios;

    public EjecutorProgramado(IPipelineStore store, IHistorialCorridas historial,
        PipelineRunner runner, IEncryptionService encryption, PipelineHydrator hydrator,
        IEnvioStore envios)
    {
        _store = store;
        _historial = historial;
        _runner = runner;
        _encryption = encryption;
        _hydrator = hydrator;
        _envios = envios;
    }

    public async Task<int> ProcesarVencidosAsync(DateTime ahora, CancellationToken ct)
    {
        var vencidos = await _store.ObtenerVencidosAsync(ahora, ct);
        var procesados = 0;

        foreach (var def in vencidos)
        {
            var runId = await _historial.IniciarAsync(def.Id, ct);
            try
            {
                await _hydrator.HydrateAsync(def, ct);
                var result = await _runner.RunAsync(def, new Dictionary<string, string>(), _encryption, ct);
                await _historial.CompletarAsync(runId, result.Success, result.RowsRead, result.RowsWritten, result.Error, ct);
                if (result.Success && result.NuevoWatermark is not null)
                    await _store.ActualizarWatermarkAsync(def.Id, result.NuevoWatermark, ct);
                // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el pipeline
                // NO ejecuta el envio, solo avisa que hay algo. Quien ejecuta es el mismo tick, via
                // EjecutorEnvios. Se prende solo si la corrida salio bien: una tanda a medias no se
                // empuja nunca.
                if (result.Success)
                    await _envios.MarcarPendientesAsync(def.Id, ct);
                procesados++;
            }
            catch (Exception ex)
            {
                await _historial.CompletarAsync(runId, false, 0, 0, ex.Message, ct);
            }
            finally
            {
                // Recalcular SIEMPRE para no reintentar en bucle. Se usa 'ahora' (tiempo logico del
                // tick) y no DateTime.Now, para ser determinista y consistente con el filtro de vencidos.
                var proxima = ScheduleCalculator.CalcularProxima(def, ahora);
                await _store.ActualizarProgramacionAsync(def.Id, ahora, proxima, ct);
            }
        }

        return procesados;
    }
}
