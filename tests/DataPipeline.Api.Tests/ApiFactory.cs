using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Api.Tests;

/// <summary>Host de test: usa archivos SQLite temporales y credenciales PullAuth conocidas.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string MetaDb { get; } = Path.Combine(Path.GetTempPath(), $"apimeta_{Guid.NewGuid():N}.db");
    public string DataDb { get; } = Path.Combine(Path.GetTempPath(), $"apidata_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Metadata", $"Data Source={MetaDb};Pooling=False");
        builder.UseSetting("ConnectionStrings:Data", $"Data Source={DataDb};Pooling=False");
        builder.UseSetting("PullAuth:Username", "pull");
        // BCrypt hash de "secreto123":
        builder.UseSetting("PullAuth:PasswordHash", BCrypt.Net.BCrypt.HashPassword("secreto123"));
        builder.UseSetting("Encryption:Key", Convert.ToBase64String(new byte[32]));
    }

    public async Task SeedPipelineConDatosAsync(string nombre, string tabla)
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPipelineStore>();
        var data = scope.ServiceProvider.GetRequiredService<IDataStore>();

        await store.UpsertAsync(new PipelineDefinition
        {
            Id = Guid.NewGuid(), Nombre = nombre, Habilitado = true,
            Programacion = TipoProgramacion.BajoDemanda,
            TablaDestino = tabla, ModoEscritura = ModoEscritura.Reemplazar, FilasPorLote = 100,
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
        }, CancellationToken.None);

        await data.EnsureTableAsync(tabla, new[] { "importe" }, ModoEscritura.Reemplazar, CancellationToken.None);
        await data.WriteBatchAsync(tabla, new RecordBatch
        {
            Columns = new[] { "importe" },
            Rows = new[] { new Record { Values = new Dictionary<string, object?> { ["importe"] = 42 } } }
        }, CancellationToken.None);
    }
}
