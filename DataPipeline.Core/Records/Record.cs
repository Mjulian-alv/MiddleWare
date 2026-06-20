namespace DataPipeline.Core.Records;

/// <summary>Un registro: diccionario columna→valor.</summary>
public sealed record Record
{
    public required IReadOnlyDictionary<string, object?> Values { get; init; }
}
