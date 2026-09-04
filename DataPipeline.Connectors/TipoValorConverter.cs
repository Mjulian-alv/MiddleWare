// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Extraido de ColumnMapTransform.Convertir para
// reusarlo al tipar la columna watermark de los origenes de archivo (Csv/Xlsx).
using System.Globalization;

namespace DataPipeline.Connectors;

/// <summary>Convierte un valor crudo a uno de los 7 tipos soportados por el cast de Paso 3.</summary>
internal static class TipoValorConverter
{
    public static object? Convertir(object val, string tipo) => tipo.ToLowerInvariant() switch
    {
        "int"     => Convert.ToInt32(val, CultureInfo.InvariantCulture),
        "long"    => Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "decimal" => Convert.ToDecimal(val, CultureInfo.InvariantCulture),
        "double"  => Convert.ToDouble(val, CultureInfo.InvariantCulture),
        "bool"    => Convert.ToBoolean(val, CultureInfo.InvariantCulture),
        "date"    => DateTime.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        "string"  => Convert.ToString(val, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo de cast no soportado: '{tipo}'.")
    };
}
