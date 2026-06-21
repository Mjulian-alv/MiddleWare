using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataPipeline.Persistence;

/// <summary>Solo para diseno (dotnet ef migrations). No se usa en runtime.</summary>
public sealed class MetadataDbContextFactory : IDesignTimeDbContextFactory<MetadataDbContext>
{
    public MetadataDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlite("Data Source=metadata.db").Options;
        return new MetadataDbContext(opts);
    }
}
