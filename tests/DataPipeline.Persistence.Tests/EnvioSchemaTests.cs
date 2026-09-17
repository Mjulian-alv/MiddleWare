// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: la tabla Envios y la columna
// EnvioId de Corridas se crean con el DDL idempotente, no con migraciones EF.
using DataPipeline.Core.Envios;
using DataPipeline.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class EnvioSchemaTests
{
    private static string NuevaDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"envsch_{Guid.NewGuid():N}.db")};Pooling=False";

    private static async Task<List<string>> ColumnasAsync(SqliteConnection con, string tabla)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabla}\");";
        await using var rd = await cmd.ExecuteReaderAsync();
        var cols = new List<string>();
        while (await rd.ReadAsync()) cols.Add(rd.GetString(1));
        return cols;
    }

    [Fact]
    public async Task Crea_la_tabla_Envios_con_todas_sus_columnas()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        var cols = await ColumnasAsync(con, "Envios");

        Assert.Contains("Id", cols);
        Assert.Contains("Nombre", cols);
        Assert.Contains("Habilitado", cols);
        Assert.Contains("PipelineId", cols);
        Assert.Contains("DataSourceId", cols);
        Assert.Contains("TablaDestino", cols);
        Assert.Contains("Pendiente", cols);
        Assert.Contains("InicioEnCurso", cols);
        Assert.Contains("UltimoCreado", cols);
        Assert.Contains("IntentosFallidos", cols);
        Assert.Contains("ProximoIntento", cols);
        Assert.Contains("UltimoEnvio", cols);
        Assert.Contains("UltimoError", cols);
    }

    [Fact]
    public async Task Agrega_EnvioId_a_Corridas()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        Assert.Contains("EnvioId", await ColumnasAsync(con, "Corridas"));
    }

    [Fact]
    public async Task Es_idempotente_sobre_una_base_que_ya_tiene_el_esquema()
    {
        await using var con = new SqliteConnection(NuevaDb());
        await con.OpenAsync();
        await MetadataSchemaInitializer.EnsureAsync(con, null);
        await MetadataSchemaInitializer.EnsureAsync(con, null);

        Assert.Contains("Pendiente", await ColumnasAsync(con, "Envios"));
    }

    [Theory]
    [InlineData(true, false, 0, EstadoEnvio.AlDia)]
    [InlineData(true, true, 0, EstadoEnvio.Pendiente)]
    [InlineData(true, true, 2, EstadoEnvio.Reintentando)]
    [InlineData(true, true, 5, EstadoEnvio.Fallido)]
    [InlineData(false, false, 5, EstadoEnvio.Fallido)]
    [InlineData(false, false, 0, EstadoEnvio.Deshabilitado)]
    public void El_estado_se_deriva_de_las_banderas(bool habilitado, bool pendiente, int intentos, EstadoEnvio esperado)
    {
        var envio = new Envio { Habilitado = habilitado, Pendiente = pendiente, IntentosFallidos = intentos };

        Assert.Equal(esperado, envio.Estado);
    }
}
