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

            // Carga incremental: si hay watermark previo, inyectarlo como parametro del origen.
            // El SQL del usuario lo referencia como @watermark. Solo se agrega cuando hay valor:
            // en la primera corrida (UltimoWatermark null) SqlServerSource declara @watermark=NULL
            // por su cuenta (ver Task 3) para que "@watermark IS NULL OR ..." traiga todo.
            var parametrosEfectivos = parameters;
            if (!string.IsNullOrWhiteSpace(def.ColumnaWatermark) && def.UltimoWatermark is not null)
            {
                parametrosEfectivos = new Dictionary<string, string>(parameters)
                {
                    ["watermark"] = def.UltimoWatermark
                };
            }

            var ctx = new RunContext
            {
                PipelineId = def.Id,
                RunId = runId,
                Parameters = parametrosEfectivos,
                Encryption = encryption,
                BatchSize = def.FilasPorLote
            };

            object? maxWatermark = null;

            // Cuenta filas leidas y trackea el maximo de la columna watermark (antes de transforms).
            async IAsyncEnumerable<RecordBatch> ContarLeidas(IAsyncEnumerable<RecordBatch> src)
            {
                await foreach (var b in src.WithCancellation(ct))
                {
                    leidas += b.Rows.Count;
                    if (!string.IsNullOrWhiteSpace(def.ColumnaWatermark))
                    {
                        foreach (var fila in b.Rows)
                        {
                            if (fila.Values.TryGetValue(def.ColumnaWatermark, out var v) && v is not null)
                            {
                                if (maxWatermark is null || Comparer<object>.Default.Compare(v, maxWatermark) > 0)
                                    maxWatermark = v;
                            }
                        }
                    }
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
                await _dataStore.WriteBatchAsync(def.TablaDestino, lote, def.ColumnasClave, def.ModoEscritura, ct);
                escritas += lote.Rows.Count;
            }

            cronometro.Stop();
            var nuevoWatermark = maxWatermark switch
            {
                null => null,
                DateTime dt => dt.ToString("O"),
                _ => Convert.ToString(maxWatermark, System.Globalization.CultureInfo.InvariantCulture)
            };
            return new RunResult(runId, true, leidas, escritas, null, cronometro.Elapsed, nuevoWatermark);
        }
        catch (Exception ex)
        {
            cronometro.Stop();
            return new RunResult(runId, false, leidas, escritas, ex.Message, cronometro.Elapsed);
        }
    }
}
