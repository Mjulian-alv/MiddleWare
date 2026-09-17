// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: mismo molde que SourceFactory.
// Hoy solo SqlServer; el dia que haya otro destino se agrega un case, no se reescribe nada.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Security;

namespace DataPipeline.Connectors.Destinos;

public sealed class DestinoFactory : IDestinoFactory
{
    public IDestinoDatos Crear(DataSource ds, IEncryptionService enc)
    {
        if (ds.Tipo != TipoDataSource.SqlServer)
            throw new NotSupportedException(
                $"El origen '{ds.Nombre}' es de tipo {ds.Tipo}: solo SqlServer sirve como destino de un envio.");

        var cs = string.IsNullOrWhiteSpace(ds.ConnectionString) ? "" : enc.Decrypt(ds.ConnectionString);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"El origen '{ds.Nombre}' no tiene cadena de conexion.");

        return new SqlServerDestino(cs);
    }
}
