using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Source a su conector concreto (Fase 1: SqlServerSource).</summary>
public sealed class SourceFactory : ISourceFactory
{
    public ISource Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Origen)
            throw new ArgumentException($"El paso '{step.Nombre}' no es un Origen.", nameof(step));

        return step.Conector switch
        {
            "SqlServerSource" => new SqlServerSource(
                Requerido(step, "connectionString"),
                Requerido(step, "sql"),
                step.Configuracion.TryGetValue("connectionStringEncrypted", out var e)
                    && bool.TryParse(e, out var b) && b),
            _ => throw new NotSupportedException($"Conector de origen no soportado en Fase 1: '{step.Conector}'.")
        };
    }

    private static string Requerido(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"El origen '{step.Nombre}' requiere la config '{clave}'.", nameof(step));
}
