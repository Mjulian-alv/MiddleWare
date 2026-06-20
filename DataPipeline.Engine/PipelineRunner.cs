using System.Diagnostics;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Arma y corre la tubería Source → IDataStore por lotes (Fase 1: sin transforms).</summary>
public sealed class PipelineRunner
{
    private readonly ISourceFactory _sourceFactory;
    private readonly IDataStore _dataStore;

    public PipelineRunner(ISourceFactory sourceFactory, IDataStore dataStore)
    {
        _sourceFactory = sourceFactory;
        _dataStore = dataStore;
    }

    public async Task<RunResult> RunAsync(
        PipelineDefinition def,
        IReadOnlyDictionary<string, string> parameters,
        IEncryptionService encryption,
        CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var cronometro = Stopwatch.StartNew();
        long leidas = 0, escritas = 0;

        try
        {
            if (def.Pasos.Any(s => s.Tipo == TipoPaso.Transformacion))
                throw new NotSupportedException("Las transformaciones (Transform) llegan en Fase 2.");

            var pasoSource = def.Pasos.SingleOrDefault(s => s.Tipo == TipoPaso.Origen)
                ?? throw new InvalidOperationException("El pipeline necesita exactamente un Origen.");

            var source = _sourceFactory.Create(pasoSource);
            var ctx = new RunContext
            {
                PipelineId = def.Id,
                RunId = runId,
                Parameters = parameters,
                Encryption = encryption,
                BatchSize = def.FilasPorLote
            };

            var tablaLista = false;
            await foreach (var lote in source.ReadAsync(ctx, ct).WithCancellation(ct))
            {
                leidas += lote.Rows.Count;
                if (lote.Rows.Count == 0) continue;

                if (!tablaLista)
                {
                    await _dataStore.EnsureTableAsync(def.TablaDestino, lote.Columns, def.ModoEscritura, ct);
                    tablaLista = true;
                }
                await _dataStore.WriteBatchAsync(def.TablaDestino, lote, ct);
                escritas += lote.Rows.Count;
            }

            cronometro.Stop();
            return new RunResult(runId, true, leidas, escritas, null, cronometro.Elapsed);
        }
        catch (Exception ex)
        {
            cronometro.Stop();
            return new RunResult(runId, false, leidas, escritas, ex.Message, cronometro.Elapsed);
        }
    }
}
