// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): nuevos tipos y
// campos de configuracion; reemplaza el FilePath sin uso (comentado "futuro (Excel)").
namespace DataPipeline.Core.DataSources;

/// <summary>Tipo de origen de datos.</summary>
public enum TipoDataSource { SqlServer, Csv, Xlsx }

/// <summary>Conexion nombrada reusable (almacen A). Secretos encriptados en reposo.</summary>
public sealed class DataSource
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public TipoDataSource Tipo { get; set; }
    public string? ConnectionString { get; set; } // encriptado
    public string? Username { get; set; }          // encriptado
    public string? Password { get; set; }          // encriptado
    public string? BaseUrl { get; set; }           // futuro (RestApi)

    /// <summary>Carpeta de red donde se dejan los exports. Solo Csv/Xlsx.</summary>
    public string? RutaCarpeta { get; set; }

    /// <summary>Glob para filtrar archivos (ej. "VENTAS_*.csv"). Vacio = "*.csv"/"*.xlsx" segun Tipo.</summary>
    public string? PatronArchivo { get; set; }

    /// <summary>Separador de campos. Solo Csv. Vacio = ",".</summary>
    public string? Separador { get; set; }

    /// <summary>Encoding del archivo ("UTF-8"/"Windows-1252"). Solo Csv. Vacio = "UTF-8".</summary>
    public string? Encoding { get; set; }

    /// <summary>Si la primera fila es encabezado. Solo Csv.</summary>
    public bool TieneEncabezado { get; set; } = true;

    /// <summary>Nombre de la hoja a leer. Solo Xlsx. Vacio = primera hoja del libro.</summary>
    public string? HojaExcel { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
