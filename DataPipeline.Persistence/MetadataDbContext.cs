using System.Text.Json;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Envios;
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

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo.
    public DbSet<Envio> Envios => Set<Envio>();

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

        var clavesConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<string>>(v, jsonOpts) ?? new());
        var clavesComparer = new ValueComparer<List<string>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.ColumnasClave).HasConversion(clavesConverter).Metadata.SetValueComparer(clavesComparer);

        var esquemaConverter = new ValueConverter<List<ColumnaOrigen>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => JsonSerializer.Deserialize<List<ColumnaOrigen>>(v, jsonOpts) ?? new());
        var esquemaComparer = new ValueComparer<List<ColumnaOrigen>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(c, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ColumnaOrigen>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        pipeline.Property(p => p.EsquemaOrigen).HasConversion(esquemaConverter).Metadata.SetValueComparer(esquemaComparer);

        var corrida = b.Entity<PipelineRun>();
        corrida.HasKey(c => c.Id);
        corrida.HasIndex(c => c.Inicio);
        corrida.Property(c => c.Estado).HasConversion<string>();

        var origen = b.Entity<DataSource>();
        origen.HasKey(d => d.Id);
        origen.HasIndex(d => d.Nombre).IsUnique();
        origen.Property(d => d.Tipo).HasConversion<string>();

        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo. Estado es derivado,
        // no tiene columna: se ignora para que EF no intente materializarlo.
        var envio = b.Entity<Envio>();
        envio.HasKey(e => e.Id);
        envio.HasIndex(e => e.Nombre).IsUnique();
        envio.HasIndex(e => e.PipelineId);
        envio.Ignore(e => e.Estado);
    }
}
