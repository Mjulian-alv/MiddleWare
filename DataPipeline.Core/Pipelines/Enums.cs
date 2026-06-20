namespace DataPipeline.Core.Pipelines;

/// <summary>Clase de un paso: lee de un origen o transforma datos en vuelo.</summary>
public enum TipoPaso { Origen, Transformacion }

/// <summary>Forma de programar un pipeline.</summary>
public enum TipoProgramacion { BajoDemanda, Diaria, Semanal, Mensual }

/// <summary>Cómo escribe el sink interno en la tabla destino.</summary>
public enum ModoEscritura { Agregar, Combinar, Reemplazar }
