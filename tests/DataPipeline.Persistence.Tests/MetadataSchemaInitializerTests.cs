using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class MetadataSchemaInitializerTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"schemainit_{Guid.NewGuid():N}.db")};Pooling=False";

    private static MetadataDbContext Ctx(string cs) =>
        new(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);

    private static async Task<HashSet<string>> ColumnasDe(SqliteConnection cn, string tabla)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{tabla}')";
        var cols = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) cols.Add(rd.GetString(1)); // col 1 = "name"
        return cols;
    }

    private static async Task Ejecutar(SqliteConnection cn, string sql)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Crea_esquema_completo_y_permite_roundtrip_de_todas_las_entidades()
    {
        var cs = NuevaDb();
        await using (var cn = new SqliteConnection(cs))
        {
            await cn.OpenAsync();
            await MetadataSchemaInitializer.EnsureAsync(cn, null);
        }

        // EF sobre la MISMA base: si el DDL olvido una columna, estos inserts fallan (drift guard).
        var pid = Guid.NewGuid();
        await using (var ctx = Ctx(cs))
        {
            ctx.Pipelines.Add(new PipelineDefinition
            {
                Id = pid, Nombre = "p1", Habilitado = true,
                Programacion = TipoProgramacion.Diaria, HoraDelDia = "08:00",
                DiaDeLaSemana = 1, DiaDelMes = 1,
                ProximaCorrida = DateTime.UtcNow, UltimaCorrida = DateTime.UtcNow,
                TablaDestino = "t", ModoEscritura = ModoEscritura.Reemplazar,
                ColumnasClave = { "id" }, CorrerDespuesDe = { Guid.NewGuid() },
                EsquemaOrigen = { new ColumnaOrigen { Nombre = "fecha", Tipo = "datetime" } },
                ColumnaWatermark = "fecha", UltimoWatermark = "2026-01-01T00:00:00.0000000",
                FilasPorLote = 10,
                Pasos = { new StepDefinition { Nombre = "o", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
            });
            ctx.Origenes.Add(new DataSource
            {
                Id = Guid.NewGuid(), Nombre = "ds1", Tipo = TipoDataSource.SqlServer,
                ConnectionString = "x", Username = "u", Password = "p",
                BaseUrl = "b", FilePath = "f", Activo = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            ctx.Corridas.Add(new PipelineRun
            {
                Id = Guid.NewGuid(), PipelineId = pid, Inicio = DateTime.UtcNow, Fin = DateTime.UtcNow,
                Estado = EstadoCorrida.Ok, FilasLeidas = 1, FilasEscritas = 1, FilasError = 0, Mensaje = "ok"
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = Ctx(cs);
        Assert.NotNull(await ctx2.Pipelines.FindAsync(pid));
        Assert.Equal(1, await ctx2.Origenes.CountAsync());
        Assert.Equal(1, await ctx2.Corridas.CountAsync());
    }

    [Fact]
    public async Task Es_idempotente_al_correr_dos_veces()
    {
        var cs = NuevaDb();
        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();

        await MetadataSchemaInitializer.EnsureAsync(cn, null);
        await MetadataSchemaInitializer.EnsureAsync(cn, null); // no debe tirar

        var cols = await ColumnasDe(cn, "Pipelines");
        Assert.Contains("ColumnaWatermark", cols);
        Assert.Contains("UltimoWatermark", cols);
    }

    [Fact]
    public async Task Agrega_columnas_faltantes_sobre_una_base_vieja()
    {
        var cs = NuevaDb();
        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();

        // Simular una base pre-4C: Pipelines con el esquema anterior (con ColumnasClave, SIN las 2 de watermark).
        await Ejecutar(cn, """
            CREATE TABLE "Pipelines" (
                "Id" TEXT NOT NULL PRIMARY KEY, "Nombre" TEXT NOT NULL, "Habilitado" INTEGER NOT NULL,
                "Programacion" TEXT NOT NULL, "HoraDelDia" TEXT NULL, "DiaDeLaSemana" INTEGER NULL,
                "DiaDelMes" INTEGER NULL, "ProximaCorrida" TEXT NULL, "UltimaCorrida" TEXT NULL,
                "Pasos" TEXT NOT NULL, "TablaDestino" TEXT NOT NULL, "ModoEscritura" TEXT NOT NULL,
                "CorrerDespuesDe" TEXT NOT NULL, "ColumnasClave" TEXT NOT NULL DEFAULT '[]', "FilasPorLote" INTEGER NOT NULL
            )
            """);

        await MetadataSchemaInitializer.EnsureAsync(cn, null);

        var cols = await ColumnasDe(cn, "Pipelines");
        Assert.Contains("ColumnaWatermark", cols);
        Assert.Contains("UltimoWatermark", cols);
    }
}
