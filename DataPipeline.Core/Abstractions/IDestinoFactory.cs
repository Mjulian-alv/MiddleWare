// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: mismo molde que ISourceFactory.
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve el destino a partir del DataSource configurado en el envio.</summary>
public interface IDestinoFactory
{
    IDestinoDatos Crear(DataSource ds, IEncryptionService enc);
}
