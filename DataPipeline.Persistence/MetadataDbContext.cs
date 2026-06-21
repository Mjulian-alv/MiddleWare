using System.Text.Json;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DataPipeline.Persistence;

/// <summary>Almacen A: definiciones de pipeline + historial de corridas.</summary>
public sealed class MetadataDbContext : DbContext
{
    public MetadataDbContext(DbContextOptions<MetadataDbContext> options) : base(options) { }

    public DbSet<PipelineDefinition> Pipelines => Set<PipelineDefinition>();
    public DbSet<PipelineRun> Corridas => Set<PipelineRun>();
    public DbSet<DataSource> Origenes => Set<DataSource>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var pipeline = b.Entity<PipelineDefinition>();
        pipeline.HasKey(p => p.Id);
        pipeline.HasIndex(p => p.Nombre).IsUnique();
        pipeline.HasIndex(p => p.ProximaCorrida);
        pipeline.Property(p => p.Programacion).HasConversion<string>();
        pipeline.Property(p => p.ModoEscritura).HasConversion<string>();

        // Colecciones como columnas JSON.
        var pasosConverter = new ValueConverter<List<StepDefinition>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<StepDefinition>>(v, jsonOpts) ?? new());
        var pasosComparer = new ValueComparer<List<StepDefinition>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<StepDefinition>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.Pasos).HasConversion(pasosConverter).Metadata.SetValueComparer(pasosComparer);

        var guidsConverter = new ValueConverter<List<Guid>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOpts) ?? new());
        var guidsComparer = new ValueComparer<List<Guid>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<Guid>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.CorrerDespuesDe).HasConversion(guidsConverter).Metadata.SetValueComparer(guidsComparer);

        var corrida = b.Entity<PipelineRun>();
        corrida.HasKey(c => c.Id);
        corrida.HasIndex(c => c.Inicio);
        corrida.Property(c => c.Estado).HasConversion<string>();

        var origen = b.Entity<DataSource>();
        origen.HasKey(d => d.Id);
        origen.HasIndex(d => d.Nombre).IsUnique();
        origen.Property(d => d.Tipo).HasConversion<string>();
    }
}
