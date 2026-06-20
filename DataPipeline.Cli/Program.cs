using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Security;
using DataPipeline.Engine;

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: DataPipeline.Cli <pipeline.json> <sqlite.db> [claveBase64]");
    return 1;
}

var jsonPath = args[0];
var dbPath = args[1];
var claveBase64 = args.Length >= 3 ? args[2] : null;

var def = PipelineJsonLoader.Load(await File.ReadAllTextAsync(jsonPath));

var store = new SqliteDataStore($"Data Source={dbPath}");
var factory = new SourceFactory();
var runner = new PipelineRunner(factory, store);

// Si no se pasa clave, se usa una dummy válida (32 bytes) porque el ejemplo no cifra la conexión.
IEncryptionService enc = new EncryptionService(claveBase64 ?? Convert.ToBase64String(new byte[32]));

var result = await runner.RunAsync(def, new Dictionary<string, string>(), enc);

Console.WriteLine(
    $"Run {result.RunId} -> Success={result.Success} " +
    $"Read={result.RowsRead} Written={result.RowsWritten} " +
    $"Dur={result.Duration} Error={result.Error}");

return result.Success ? 0 : 2;
