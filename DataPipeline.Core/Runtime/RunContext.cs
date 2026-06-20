using DataPipeline.Core.Security;

namespace DataPipeline.Core.Runtime;

/// <summary>Contexto de una corrida: identidad, parámetros, secretos y tamaño de lote.</summary>
public sealed class RunContext
{
    public required Guid PipelineId { get; init; }
    public required Guid RunId { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required IEncryptionService Encryption { get; init; }
    public int BatchSize { get; init; } = 5_000;
}
