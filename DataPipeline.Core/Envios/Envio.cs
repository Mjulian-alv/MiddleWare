// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: entidad nueva del almacen A.
// El envio no tiene programacion propia: se cuelga de un pipeline (PipelineId) y hereda su cadencia.
// El disparo es la bandera Pendiente, que el pipeline prende al terminar bien y el tick del scheduler
// consume. UltimoCreado es el cursor sobre la columna _creado de la tabla intermedia.
namespace DataPipeline.Core.Envios;

/// <summary>Estado visible de un envio. Derivado, no persistido.</summary>
public enum EstadoEnvio { AlDia, Pendiente, Reintentando, Fallido, Deshabilitado }

/// <summary>Push del resultado de un pipeline a una tabla nuestra en un SQL Server remoto.</summary>
public sealed class Envio
{
    /// <summary>Tope de fallos totales. Al alcanzarlo el envio queda Fallido y se deshabilita solo.</summary>
    public const int MaxIntentos = 5;

    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Habilitado { get; set; } = true;

    /// <summary>Pipeline duenno. De el hereda la cadencia: el envio no tiene horario propio.</summary>
    public Guid PipelineId { get; set; }

    /// <summary>Conexion al SQL remoto. Es un DataSource de tipo SqlServer usado como destino.</summary>
    public Guid DataSourceId { get; set; }

    /// <summary>Nombre de la tabla en el servidor remoto. La crea y la mantiene DataPipeline.</summary>
    public string TablaDestino { get; set; } = "";

    /// <summary>Bandera del disparo: hay filas para empujar.</summary>
    public bool Pendiente { get; set; }

    /// <summary>Anti-solape. Null = libre.</summary>
    public DateTime? InicioEnCurso { get; set; }

    /// <summary>Cursor: maximo _creado ya empujado. Avanza solo al terminar el envio completo.</summary>
    public DateTime? UltimoCreado { get; set; }

    public int IntentosFallidos { get; set; }
    public DateTime? ProximoIntento { get; set; }
    public DateTime? UltimoEnvio { get; set; }
    public string? UltimoError { get; set; }

    /// <summary>
    /// Estado para la grilla. Fallido gana sobre Deshabilitado porque al agotar los reintentos el
    /// envio se deshabilita solo: hay que poder distinguirlo del que alguien apago a mano.
    /// </summary>
    public EstadoEnvio Estado =>
        IntentosFallidos >= MaxIntentos ? EstadoEnvio.Fallido
        : !Habilitado ? EstadoEnvio.Deshabilitado
        : IntentosFallidos > 0 ? EstadoEnvio.Reintentando
        : Pendiente ? EstadoEnvio.Pendiente
        : EstadoEnvio.AlDia;
}
