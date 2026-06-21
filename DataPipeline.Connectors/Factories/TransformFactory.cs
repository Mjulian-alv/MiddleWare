using System.Text.Json;
using DataPipeline.Connectors.Transforms;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;

namespace DataPipeline.Connectors.Factories;

/// <summary>Resuelve un StepDefinition de tipo Transformacion (Fase 2: ColumnMapTransform).</summary>
public sealed class TransformFactory : ITransformFactory
{
    public ITransform Create(StepDefinition step)
    {
        if (step.Tipo != TipoPaso.Transformacion)
            throw new ArgumentException($"El paso '{step.Nombre}' no es una Transformacion.", nameof(step));

        return step.Conector switch
        {
            "ColumnMapTransform" => new ColumnMapTransform(
                LeerMapa(step, "rename"),
                LeerMapa(step, "cast"),
                LeerMapa(step, "computed"),
                LeerStringOpcional(step, "filter")),
            _ => throw new NotSupportedException($"Conector de transformacion no soportado: '{step.Conector}'.")
        };
    }

    private static Dictionary<string, string> LeerMapa(StepDefinition step, string clave)
    {
        var dst = new Dictionary<string, string>();
        if (step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var prop in v.EnumerateObject())
                dst[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
        return dst;
    }

    private static string? LeerStringOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
