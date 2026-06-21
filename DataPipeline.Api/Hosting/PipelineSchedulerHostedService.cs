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
                var ejecutor = scope.ServiceProvider.GetRequiredService<EjecutorProgramado>();
                var n = await ejecutor.ProcesarVencidosAsync(DateTime.Now, stoppingToken);
                if (n > 0) _logger.LogInformation("PipelineScheduler: {N} pipeline(s) procesado(s).", n);
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
