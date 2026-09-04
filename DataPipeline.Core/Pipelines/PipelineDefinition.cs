namespace DataPipeline.Core.Pipelines;

/// <summary>Definición declarativa de un pipeline (lo que se persiste/edita).</summary>
public sealed class PipelineDefinition
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Habilitado { get; set; } = true;

    public TipoProgramacion Programacion { get; set; } = TipoProgramacion.BajoDemanda;
    public string? HoraDelDia { get; set; }
    public int? DiaDeLaSemana { get; set; }
    public int? DiaDelMes { get; set; }
    public DateTime? ProximaCorrida { get; set; }
    public DateTime? UltimaCorrida { get; set; }

    public List<StepDefinition> Pasos { get; set; } = new();
    public string TablaDestino { get; set; } = "";
    public ModoEscritura ModoEscritura { get; set; } = ModoEscritura.Reemplazar;

    public List<Guid> CorrerDespuesDe { get; set; } = new();

    /// <summary>Columnas de negocio que forman la clave de fila del serving (upsert + ack).</summary>
    public List<string> ColumnasClave { get; set; } = new();

    /// <summary>Columnas del resultado del origen, descubiertas en diseno (ayuda para watermark/clave).</summary>
    public List<ColumnaOrigen> EsquemaOrigen { get; set; } = new();

    /// <summary>Columna del origen cuyo maximo se trackea para carga incremental. Vacio = no incremental.</summary>
    public string? ColumnaWatermark { get; set; }

    /// <summary>Ultimo maximo visto de <see cref="ColumnaWatermark"/> (persistido tras cada corrida exitosa).</summary>
    public string? UltimoWatermark { get; set; }

    // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): el archivo no
    // tiene tipos como SQL Server; sin esto, comparar una fecha o un numero como texto compara mal
    // ("9" > "10"). Uno de int/long/decimal/double/bool/date/string (mismos 7 valores que el cast
    // de Paso 3). Ignorado para SqlServer (ADO.NET ya entrega valores tipados).
    public string? ColumnaWatermarkTipo { get; set; }

    public int FilasPorLote { get; set; } = 5_000;
}
