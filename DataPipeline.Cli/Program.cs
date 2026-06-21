using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length >= 1 && args[0] == "import")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Uso: DataPipeline.Cli import <pipeline.json> <metadata.db>");
        return 1;
    }
    var defImport = PipelineJsonLoader.Load(await File.ReadAllTextAsync(args[1]));
    defImport.ProximaCorrida = ScheduleCalculator.CalcularProxima(defImport, DateTime.Now);

    var opts = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite($"Data Source={args[2]}").Options;
    using var ctx = new MetadataDbContext(opts);
    await ctx.Database.EnsureCreatedAsync();
    var store = new EfPipelineStore(ctx);
    await store.UpsertAsync(defImport, CancellationToken.None);

    Console.WriteLine($"Importado '{defImport.Nombre}' (Id {defImport.Id}), proxima corrida: {defImport.ProximaCorrida}");
    return 0;
}

// Modo run directo (Fase 1): <pipeline.json> <sqlite.db> [claveBase64]
if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: DataPipeline.Cli <pipeline.json> <sqlite.db> [claveBase64]");
    Console.Error.WriteLine("     DataPipeline.Cli import <pipeline.json> <metadata.db>");
    return 1;
}

var def = PipelineJsonLoader.Load(await File.ReadAllTextAsync(args[0]));
var data = new SqliteDataStore($"Data Source={args[1]}");
var runner = new PipelineRunner(new SourceFactory(), new TransformFactory(), data);
IEncryptionService enc = new EncryptionService(args.Length >= 3 ? args[2] : Convert.ToBase64String(new byte[32]));

var result = await runner.RunAsync(def, new Dictionary<string, string>(), enc);
Console.WriteLine(
    $"Run {result.RunId} -> Success={result.Success} Read={result.RowsRead} " +
    $"Written={result.RowsWritten} Dur={result.Duration} Error={result.Error}");
return result.Success ? 0 : 2;
