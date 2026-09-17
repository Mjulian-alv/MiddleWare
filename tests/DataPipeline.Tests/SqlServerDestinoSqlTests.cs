// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: el armado del SQL se testea
// puro, sin servidor. MERGE esta prohibido (SQL Server viejo, mismo criterio que TAREA 00026).
using DataPipeline.Connectors.Destinos;
using DataPipeline.Core.Abstractions;
using Xunit;

namespace DataPipeline.Tests;

public class SqlServerDestinoSqlTests
{
    private static readonly ColumnaDestino[] Columnas =
    {
        new("id", typeof(long)),
        new("monto", typeof(decimal)),
        new("fecha", typeof(DateTime)),
        new("activo", typeof(bool)),
        new("nombre", typeof(string)),
        new("sinDato", null)
    };

    [Theory]
    [InlineData(typeof(int), "BIGINT")]
    [InlineData(typeof(long), "BIGINT")]
    [InlineData(typeof(decimal), "DECIMAL(38,10)")]
    [InlineData(typeof(double), "FLOAT")]
    [InlineData(typeof(bool), "BIT")]
    [InlineData(typeof(DateTime), "DATETIME2")]
    [InlineData(typeof(string), "NVARCHAR(MAX)")]
    [InlineData(null, "NVARCHAR(MAX)")]
    public void Mapea_el_tipo_clr_al_tipo_sql(Type? clr, string esperado) =>
        Assert.Equal(esperado, SqlServerDestinoSql.TipoSql(clr));

    [Fact]
    public void Crear_tabla_es_idempotente_y_agrega_las_columnas_de_control()
    {
        var sql = SqlServerDestinoSql.CrearTabla("dbo.mw_x", Columnas);

        Assert.Contains("IF OBJECT_ID(N'[dbo].[mw_x]', N'U') IS NULL", sql);
        Assert.Contains("[id] BIGINT NULL", sql);
        Assert.Contains("[monto] DECIMAL(38,10) NULL", sql);
        Assert.Contains("[sinDato] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("[_key] NVARCHAR(450) NOT NULL", sql);
        Assert.Contains("[_creado] DATETIME2 NOT NULL", sql);
        Assert.Contains("CONSTRAINT [PK_mw_x] PRIMARY KEY ([_key])", sql);
    }

    [Fact]
    public void Crear_tabla_no_lleva_la_columna_procesado()
    {
        // _procesado es el ack de NUESTROS consumidores PULL: del otro lado no significa nada.
        Assert.DoesNotContain("_procesado", SqlServerDestinoSql.CrearTabla("dbo.mw_x", Columnas));
    }

    [Fact]
    public void Upsert_usa_IF_EXISTS_y_no_MERGE()
    {
        var sql = SqlServerDestinoSql.Upsert("dbo.mw_x", new[] { "id", "monto", "_key", "_creado" });

        Assert.Contains("IF EXISTS", sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("ELSE", sql);
        Assert.Contains("INSERT", sql);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upsert_matchea_por_key_y_no_lo_pisa_en_el_update()
    {
        var sql = SqlServerDestinoSql.Upsert("dbo.mw_x", new[] { "id", "_key", "_creado" });

        Assert.Contains("WHERE [_key] = @p_key", sql);
        Assert.Contains("[id] = @p0", sql);
        Assert.Contains("[_creado] = @p2", sql);
        Assert.DoesNotContain("[_key] = @p_key,", sql);
        Assert.DoesNotContain("SET [_key]", sql);
    }

    [Fact]
    public void Upsert_inserta_todas_las_columnas_con_su_parametro()
    {
        var sql = SqlServerDestinoSql.Upsert("dbo.mw_x", new[] { "id", "_key", "_creado" });

        Assert.Contains("INSERT INTO [dbo].[mw_x] ([id], [_key], [_creado]) VALUES (@p0, @p_key, @p2);", sql);
    }

    [Theory]
    [InlineData("dbo.mw_x", "dbo", "mw_x")]
    [InlineData("mw_x", "dbo", "mw_x")]
    [InlineData("[ventas].[mw_x]", "ventas", "mw_x")]
    public void Parte_el_nombre_en_esquema_y_tabla(string entrada, string esquema, string tabla)
    {
        var partes = SqlServerDestinoSql.Partes(entrada);

        Assert.Equal(esquema, partes.Esquema);
        Assert.Equal(tabla, partes.Tabla);
    }

    [Fact]
    public void Escapa_los_corchetes_del_identificador()
    {
        var sql = SqlServerDestinoSql.CrearTabla("dbo.mal]nombre", Columnas);

        Assert.Contains("[mal]]nombre]", sql);
    }

    [Theory]
    [InlineData("[mi.esquema].[t]", "mi.esquema", "t")]
    [InlineData("[dbo].[con]]corchete]", "dbo", "con]corchete")]
    public void Entre_corchetes_respeta_puntos_y_escapes(string entrada, string esquema, string tabla)
    {
        var partes = SqlServerDestinoSql.Partes(entrada);

        Assert.Equal(esquema, partes.Esquema);
        Assert.Equal(tabla, partes.Tabla);
    }

    [Theory]
    [InlineData("otra_base.dbo.mw_x")]
    [InlineData("dbo.")]
    [InlineData("")]
    public void Rechaza_nombres_que_no_son_tabla_o_esquema_tabla(string entrada) =>
        Assert.Throws<ArgumentException>(() => SqlServerDestinoSql.Partes(entrada));
}
