using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length >= 1 && args[0] == "seed-demo")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Uso: DataPipeline.Cli seed-demo <metadata.db> <data.db>");
        return 1;
    }

    // 1) Registrar el pipeline 'demo' en el almacen A (sin necesidad de origen real).
    var optsMeta = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite($"Data Source={args[1]}").Options;
    await using (var ctxMeta = new MetadataDbContext(optsMeta))
    {
        await MetadataSchemaInitializer.EnsureAsync(ctxMeta.Database.GetDbConnection(), null);
        var pstore = new EfPipelineStore(ctxMeta);
        var demo = await pstore.GetByNombreAsync("demo", CancellationToken.None) ?? new PipelineDefinition();
        demo.Nombre = "demo";
        demo.Habilitado = true;
        demo.Programacion = TipoProgramacion.BajoDemanda;
        demo.TablaDestino = "serving_demo";
        demo.ModoEscritura = ModoEscritura.Agregar;
        demo.ColumnasClave = new() { "id" };
        demo.FilasPorLote = 100;
        demo.Pasos = new() { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } };
        await pstore.UpsertAsync(demo, CancellationToken.None);
    }

    // 2) Sembrar filas de ejemplo en el serving (con columnas de control _key/_creado/_procesado).
    var demoStore = new SqliteDataStore($"Data Source={args[2]}");
    var cols = new[] { "id", "producto", "monto" };
    await demoStore.EnsureTableAsync("serving_demo", cols, ModoEscritura.Agregar, CancellationToken.None);
    var filas = new[]
    {
        new Record { Values = new Dictionary<string, object?> { ["id"] = 1, ["producto"] = "Cafe", ["monto"] = 1500 } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 2, ["producto"] = "Te", ["monto"] = 1200 } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 3, ["producto"] = "Mate", ["monto"] = 800 } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 4, ["producto"] = "Azucar", ["monto"] = 600 } },
        new Record { Values = new Dictionary<string, object?> { ["id"] = 5, ["producto"] = "Galletas", ["monto"] = 2000 } },
    };
    await demoStore.WriteBatchAsync("serving_demo", new RecordBatch { Columns = cols, Rows = filas },
        new[] { "id" }, ModoEscritura.Agregar, CancellationToken.None);

    Console.WriteLine($"Demo sembrada: pipeline 'demo' (tabla serving_demo) con {filas.Length} filas. " +
        "Probar: GET /api/data/demo (auth pull).");
    return 0;
}

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
    await MetadataSchemaInitializer.EnsureAsync(ctx.Database.GetDbConnection(), null);
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
