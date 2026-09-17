using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
// El proyecto de tests tiene un global using Xunit; alias para evitar CS0104 (Record vs Xunit.Record).
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests.Fakes;

/// <summary>Source de prueba que emite filas predefinidas en lotes de tamaño fijo.</summary>
internal sealed class FakeSource : ISource
{
    private readonly IReadOnlyList<string> _cols;
    private readonly IReadOnlyList<Record> _filas;
    private readonly int _porLote;

    public FakeSource(IReadOnlyList<string> cols, IReadOnlyList<Record> filas, int porLote)
    {
        _cols = cols; _filas = filas; _porLote = porLote;
    }

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: simula un origen caido
    // para verificar que una corrida fallida no dispara envios. Null = no falla (comportamiento previo).
    public Exception? FallaAlLeer { get; set; }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        if (FallaAlLeer is not null) throw FallaAlLeer;
        for (int i = 0; i < _filas.Count; i += _porLote)
        {
            await Task.Yield();
            var slice = _filas.Skip(i).Take(_porLote).ToList();
            yield return new RecordBatch { Columns = _cols, Rows = slice };
        }
    }
}

/// <summary>Factory de prueba que devuelve siempre el mismo FakeSource.</summary>
internal sealed class FakeSourceFactory : ISourceFactory
{
    private readonly ISource _source;
    public FakeSourceFactory(ISource source) => _source = source;
    public ISource Create(StepDefinition step) => _source;
}

/// <summary>Source de prueba que registra los parametros recibidos (para verificar la inyeccion del watermark).</summary>
internal sealed class SpyParamsSource : ISource
{
    private readonly IReadOnlyList<string> _cols;
    public IReadOnlyDictionary<string, string> ParametrosVistos { get; private set; } =
        new Dictionary<string, string>();

    public SpyParamsSource(IReadOnlyList<string> cols) => _cols = cols;

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        ParametrosVistos = ctx.Parameters;
        await Task.Yield();
        yield return new RecordBatch { Columns = _cols, Rows = Array.Empty<Record>() };
    }
}
