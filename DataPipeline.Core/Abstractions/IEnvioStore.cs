// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: persistencia de envios.
using DataPipeline.Core.Envios;

namespace DataPipeline.Core.Abstractions;

/// <summary>Envios del almacen A. La toma de pendientes es atomica (anti-solape).</summary>
public interface IEnvioStore
{
    Task<Envio?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Envio>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Envio>> GetByPipelineAsync(Guid pipelineId, CancellationToken ct);
    Task UpsertAsync(Envio envio, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Prende Pendiente en los envios habilitados del pipeline. Devuelve cuantos marco.</summary>
    Task<int> MarcarPendientesAsync(Guid pipelineId, CancellationToken ct);

    /// <summary>
    /// Toma los envios listos para correr y los deja EnCurso en la misma operacion. Solo devuelve
    /// los que efectivamente gano: un envio ya tomado por otro barrido no vuelve a salir.
    /// </summary>
    Task<IReadOnlyList<Envio>> TomarPendientesAsync(DateTime ahora, CancellationToken ct);

    /// <summary>Suelta el EnCurso sin tocar nada mas (para una cancelacion limpia).</summary>
    Task LiberarAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Cierra un envio exitoso. ultimoCreado null = no hubo filas y todavia no hay cursor: se guarda
    /// null y no DateTime.MinValue, que la pantalla mostraria como 01/01/0001.
    /// </summary>
    Task RegistrarExitoAsync(Guid id, DateTime? ultimoCreado, DateTime fin, CancellationToken ct);

    Task RegistrarFalloAsync(Guid id, string error, DateTime? proximoIntento, bool deshabilitar, CancellationToken ct);

    /// <summary>Vuelve a habilitar un envio fallido y lo deja pendiente (boton Reintentar).</summary>
    Task ReactivarAsync(Guid id, CancellationToken ct);
}
