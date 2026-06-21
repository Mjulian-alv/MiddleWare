using System.Text.Json;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Origen a su conector concreto (Fase 1-2: SqlServerSource).</summary>
public sealed class SourceFactory : ISourceFactory
{
    public ISource Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Origen)
            throw new ArgumentException($"El paso '{step.Nombre}' no es un Origen.", nameof(step));

        return step.Conector switch
        {
            "SqlServerSource" => new SqlServerSource(
                LeerString(step, "connectionString"),
                LeerString(step, "sql"),
                step.Configuracion.TryGetValue("connectionStringEncrypted", out var e)
                    && e.ValueKind == JsonValueKind.True),
            _ => throw new NotSupportedException($"Conector de origen no soportado: '{step.Conector}'.")
        };
    }

    private static string LeerString(StepDefinition step, string clave)
    {
        if (step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        throw new ArgumentException($"El origen '{step.Nombre}' requiere la config string '{clave}'.", nameof(step));
    }
}
