using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Core.Abstractions;

/// <summary>Transforma lote a lote. (Sin implementaciones en Fase 1.)</summary>
public interface ITransform
{
    IAsyncEnumerable<RecordBatch> TransformAsync(
        IAsyncEnumerable<RecordBatch> input, RunContext ctx, CancellationToken ct);
}
