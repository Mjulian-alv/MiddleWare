using DataPipeline.Engine;

namespace DataPipeline.Api.Hosting;

/// <summary>Revisa cada ~60s los pipelines vencidos y los corre.</summary>
public sealed class PipelineSchedulerHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PipelineSchedulerHostedService> _logger;

    public PipelineSchedulerHostedService(IServiceProvider services, ILogger<PipelineSchedulerHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("PipelineScheduler iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var ahora = DateTime.Now;

                var ejecutor = scope.ServiceProvider.GetRequiredService<EjecutorProgramado>();
                var n = await ejecutor.ProcesarVencidosAsync(ahora, stoppingToken);
                if (n > 0) _logger.LogInformation("PipelineScheduler: {N} pipeline(s) procesado(s).", n);

                // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de
                // envios va DESPUES de los pipelines en el mismo tick, asi lo que acaba de aterrizar
                // sale en la misma pasada en vez de esperar 60s. Se pasa el mismo 'ahora' que los
                // pipelines para que el backoff sea determinista.
                var envios = scope.ServiceProvider.GetRequiredService<EjecutorEnvios>();
                var e = await envios.ProcesarPendientesAsync(ahora, stoppingToken);
                if (e > 0) _logger.LogInformation("PipelineScheduler: {N} envio(s) procesado(s).", e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PipelineScheduler: error en el ciclo.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
