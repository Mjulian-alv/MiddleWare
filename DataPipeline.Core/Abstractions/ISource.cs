using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Core.Abstractions;

/// <summary>Lee del origen en lotes, sin materializar todo en memoria.</summary>
public interface ISource
{
    IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, CancellationToken ct);
}
