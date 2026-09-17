// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: destino de mentira para
// testear el ejecutor sin un SQL Server.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Security;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests.Fakes;

internal sealed class FakeDestino : IDestinoDatos
{
    public List<Record> Escritas { get; } = new();
    public List<ColumnaDestino> ColumnasCreadas { get; } = new();
    public Exception? FallaAlEscribir { get; set; }
    public int VecesEnsure { get; private set; }

    public Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct)
    {
        VecesEnsure++;
        ColumnasCreadas.Clear();
        ColumnasCreadas.AddRange(columnas);
        return Task.CompletedTask;
    }

    public Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct)
    {
        if (FallaAlEscribir is not null) throw FallaAlEscribir;
        Escritas.AddRange(lote.Rows);
        return Task.FromResult((long)lote.Rows.Count);
    }
}

internal sealed class FakeDestinoFactory : IDestinoFactory
{
    private readonly IDestinoDatos _destino;
    public FakeDestinoFactory(IDestinoDatos destino) => _destino = destino;
    public IDestinoDatos Crear(DataSource ds, IEncryptionService enc) => _destino;
}
