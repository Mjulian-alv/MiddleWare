using DataPipeline.Api.Auth;
using DataPipeline.Connectors.Factories;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Stores;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Security;
using DataPipeline.Engine;
using DataPipeline.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var metaCs = builder.Configuration.GetConnectionString("Metadata") ?? "Data Source=metadata.db";
var dataCs = builder.Configuration.GetConnectionString("Data") ?? "Data Source=data.db";

builder.Services.AddDbContext<MetadataDbContext>(o => o.UseSqlite(metaCs));
builder.Services.AddScoped<IPipelineStore, EfPipelineStore>();
builder.Services.AddScoped<IHistorialCorridas, EfHistorialCorridas>();
builder.Services.AddScoped<IDataSourceStore, EfDataSourceStore>();
builder.Services.AddScoped<IConnectionTester, DataPipeline.Connectors.Testing.ConnectionTester>();
builder.Services.AddScoped<ISourceFactory, SourceFactory>();
builder.Services.AddScoped<ITransformFactory, TransformFactory>();
builder.Services.AddScoped<IDataStore>(_ => new SqliteDataStore(dataCs));
builder.Services.AddScoped<PipelineRunner>();
builder.Services.AddScoped<PipelineHydrator>();
builder.Services.AddScoped<EjecutorProgramado>();
builder.Services.AddSingleton<IEncryptionService>(sp =>
    new EncryptionService(sp.GetRequiredService<IConfiguration>()["Encryption:Key"]
        ?? Convert.ToBase64String(new byte[32])));

builder.Services.AddAuthentication(BasicAuthHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>(BasicAuthHandler.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHostedService<DataPipeline.Api.Hosting.PipelineSchedulerHostedService>();

var app = builder.Build();

// Poner al dia el esquema del almacen A al arrancar (idempotente, seguro sobre bases existentes).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await MetadataSchemaInitializer.EnsureAsync(db.Database.GetDbConnection(), logger);
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { } // para WebApplicationFactory<Program>
