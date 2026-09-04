using System.Text.Json;
using DataPipeline.Connectors.Security;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Engine;
using DataPipeline.Tests.Fakes;
using Xunit;

namespace DataPipeline.Tests;

public class PipelineHydratorTests
{
    private static EncryptionService Enc() => new(Convert.ToBase64String(new byte[32]));
    private static JsonElement S(string v) => JsonSerializer.SerializeToElement(v);

    [Fact]
    public async Task Inyecta_connectionString_desencriptada_desde_el_datasource()
    {
        var enc = Enc();
        var id = Guid.NewGuid();
        var ds = new DataSource { Id = id, Nombre = "ventas", Tipo = TipoDataSource.SqlServer,
            ConnectionString = enc.Encrypt("Server=real;Database=db;") };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), enc);

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()), ["sql"] = S("SELECT 1") } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.True(cfg.ContainsKey("connectionString"));
        Assert.Equal("Server=real;Database=db;", cfg["connectionString"].GetString());
    }

    [Fact]
    public async Task Paso_con_config_inline_queda_intacto()
    {
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(), Enc());
        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource",
                Configuracion = new() { ["connectionString"] = S("Server=inline;"), ["sql"] = S("SELECT 1") } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        Assert.Equal("Server=inline;", def.Pasos[0].Configuracion["connectionString"].GetString());
    }

    [Fact]
    public async Task Inyecta_config_de_carpeta_para_un_DataSource_Csv()
    {
        var id = Guid.NewGuid();
        var ds = new DataSource
        {
            Id = id, Nombre = "ventas-csv", Tipo = TipoDataSource.Csv,
            RutaCarpeta = @"\\erp\exports", PatronArchivo = "VENTAS_*.csv",
            Separador = ";", Encoding = "Windows-1252", TieneEncabezado = true
        };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), Enc());

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "CsvSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()) } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.Equal(@"\\erp\exports", cfg["rutaCarpeta"].GetString());
        Assert.Equal("VENTAS_*.csv", cfg["patronArchivo"].GetString());
        Assert.Equal(";", cfg["separador"].GetString());
        Assert.Equal("Windows-1252", cfg["encoding"].GetString());
        Assert.True(cfg["tieneEncabezado"].GetBoolean());
        Assert.False(cfg.ContainsKey("connectionString"));
    }

    [Fact]
    public async Task Inyecta_config_de_hoja_para_un_DataSource_Xlsx()
    {
        var id = Guid.NewGuid();
        var ds = new DataSource
        {
            Id = id, Nombre = "ventas-xlsx", Tipo = TipoDataSource.Xlsx,
            RutaCarpeta = @"\\erp\exports", HojaExcel = "Datos"
        };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), Enc());

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "XlsxSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()) } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.Equal("Datos", cfg["hojaExcel"].GetString());
    }
}
