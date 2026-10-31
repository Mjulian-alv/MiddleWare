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

    // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 1 - Origen DataHub CompLook. Lo importante no es solo
    // que inyecte lo suyo, sino que NO caiga por la rama de archivo: antes el hydrator era un if
    // binario y un tipo nuevo se iba a "archivo" con rutaCarpeta vacia, fallando recien al correr.
    [Fact]
    public async Task Inyecta_baseUrl_y_la_credencial_de_datos_desencriptada()
    {
        var enc = Enc();
        var id = Guid.NewGuid();
        var ds = new DataSource
        {
            Id = id, Nombre = "datahub", Tipo = TipoDataSource.DataHubCompLook,
            BaseUrl = "http://srv-datahub:5000",
            Username = enc.Encrypt("dataPipeline"),
            Password = enc.Encrypt("secreta"),
            UsuarioCatalogo = enc.Encrypt("consuAdmin"),
            PasswordCatalogo = enc.Encrypt("otra")
        };
        var hydrator = new PipelineHydrator(new FakeDataSourceStore(ds), enc);

        var def = new PipelineDefinition
        {
            Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "DataHubSource",
                Configuracion = new() { ["dataSourceId"] = S(id.ToString()), ["ruta"] = S("ventas/facturas") } } }
        };

        await hydrator.HydrateAsync(def, CancellationToken.None);

        var cfg = def.Pasos[0].Configuracion;
        Assert.Equal("http://srv-datahub:5000", cfg["baseUrl"].GetString());
        Assert.Equal("dataPipeline", cfg["usuario"].GetString());
        Assert.Equal("secreta", cfg["password"].GetString());

        // La credencial del catalogo es de diseno: no tiene por que viajar a la corrida.
        Assert.False(cfg.ContainsKey("usuarioCatalogo"));
        Assert.False(cfg.ContainsKey("passwordCatalogo"));

        // Y nada de la rama de archivo.
        Assert.False(cfg.ContainsKey("rutaCarpeta"));
        Assert.False(cfg.ContainsKey("patronArchivo"));
    }
}
