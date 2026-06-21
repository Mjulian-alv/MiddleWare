namespace DataPipeline.Core.DataSources;

/// <summary>Tipo de origen de datos. Fase 3 arranca con SqlServer; extensible.</summary>
public enum TipoDataSource { SqlServer }

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
    public string? FilePath { get; set; }          // futuro (Excel)
    public bool Activo { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
