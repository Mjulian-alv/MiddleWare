using System.Diagnostics;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>Arma y corre la tubería Source -> Transform(s) -> IDataStore por lotes.</summary>
public sealed class PipelineRunner
{
    private readonly ISourceFactory _sourceFactory;
    private readonly ITransformFactory _transformFactory;
    private readonly IDataStore _dataStore;

    public PipelineRunner(ISourceFactory sourceFactory, ITransformFactory transformFactory, IDataStore dataStore)
    {
        _sourceFactory = sourceFactory;
        _transformFactory = transformFactory;
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
            var pasoSource = def.Pasos.SingleOrDefault(s => s.Tipo == TipoPaso.Origen)
                ?? throw new InvalidOperationException("El pipeline necesita exactamente un Origen.");

            var source = _sourceFactory.Create(pasoSource);
            var transforms = def.Pasos
                .Where(s => s.Tipo == TipoPaso.Transformacion)
                .Select(_transformFactory.Create)
                .ToList();

            var ctx = new RunContext
            {
                PipelineId = def.Id,
                RunId = runId,
                Parameters = parameters,
                Encryption = encryption,
                BatchSize = def.FilasPorLote
            };

            // Cuenta filas leidas del origen ANTES de los transforms (filter/etc. pueden reducirlas).
            async IAsyncEnumerable<RecordBatch> ContarLeidas(IAsyncEnumerable<RecordBatch> src)
            {
                await foreach (var b in src.WithCancellation(ct))
                {
                    leidas += b.Rows.Count;
                    yield return b;
                }
            }

            IAsyncEnumerable<RecordBatch> stream = ContarLeidas(source.ReadAsync(ctx, ct));
            foreach (var t in transforms)
                stream = t.TransformAsync(stream, ctx, ct);

            var tablaLista = false;
            await foreach (var lote in stream.WithCancellation(ct))
            {
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
