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
            // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx).
            "CsvSource" => new CsvSource(
                LeerString(step, "rutaCarpeta"),
                LeerStringOpcional(step, "patronArchivo"),
                LeerStringOpcional(step, "columnaWatermark"),
                LeerStringOpcional(step, "tipoColumnaWatermark"),
                LeerStringOpcional(step, "separador"),
                LeerStringOpcional(step, "encoding"),
                LeerBoolOpcional(step, "tieneEncabezado") ?? true),
            // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - XlsxSource resuelto por SourceFactory.
            "XlsxSource" => new XlsxSource(
                LeerString(step, "rutaCarpeta"),
                LeerStringOpcional(step, "patronArchivo"),
                LeerStringOpcional(step, "columnaWatermark"),
                LeerStringOpcional(step, "tipoColumnaWatermark"),
                LeerStringOpcional(step, "hojaExcel")),
            // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 1 - Origen DataHub CompLook. Las credenciales
            // llegan en texto plano: las desencripta PipelineHydrator antes de que corra la factory.
            "DataHubSource" => new DataHubSource(
                LeerString(step, "baseUrl"),
                LeerString(step, "ruta"),
                LeerStringOpcional(step, "metodoHttp") ?? "GET",
                LeerDiccionario(step, "parametros"),
                LeerStringOpcional(step, "esquema"),
                LeerStringOpcional(step, "usuario") ?? "",
                LeerStringOpcional(step, "password") ?? ""),
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

    private static string? LeerStringOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Los parametros del endpoint viajan como objeto
    // JSON {nombre: valor}. Todo se pasa como texto porque asi los recibe DataHub.
    private static IReadOnlyDictionary<string, string> LeerDiccionario(StepDefinition step, string clave)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!step.Configuracion.TryGetValue(clave, out var v) || v.ValueKind != JsonValueKind.Object)
            return d;

        foreach (var p in v.EnumerateObject())
        {
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => p.Value.ToString()
            };
        }
        return d;
    }

    private static bool? LeerBoolOpcional(StepDefinition step, string clave) =>
        step.Configuracion.TryGetValue(clave, out var v) &&
        (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : (bool?)null;
}
