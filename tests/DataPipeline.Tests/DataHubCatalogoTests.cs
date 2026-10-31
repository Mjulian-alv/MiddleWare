// Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Catalogo de endpoints de DataHub.
// El riesgo real aca es el mapeo: DataHub no configura JsonStringEnumConverter, asi que el tipo de
// cada parametro viaja como NUMERO (0=Text, 1=Number, 2=Date, 3=DateTime). Si eso se lee mal, el
// Admin dibuja el control equivocado y nadie se entera hasta que alguien carga una fecha a mano.
using System.Net;
using System.Text;
using DataPipeline.Connectors.Testing;
using DataPipeline.Core.Abstractions;
using Xunit;

namespace DataPipeline.Tests;

public class DataHubCatalogoTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _cuerpo;
        private readonly HttpStatusCode _codigo;
        public HttpRequestMessage? Pedido { get; private set; }

        public Handler(string cuerpo, HttpStatusCode codigo = HttpStatusCode.OK)
        {
            _cuerpo = cuerpo;
            _codigo = codigo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedido = r;
            return Task.FromResult(new HttpResponseMessage(_codigo)
            {
                Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json")
            });
        }
    }

    private const string Catalogo = """
        [
          {"id":1,"route":"ventas/facturas","httpMethod":"GET","description":"Facturas emitidas",
           "dataSourceName":"Oracle - Produccion","isActive":true,
           "parameterDefinitions":[
             {"name":"sucursal","type":0,"label":"Sucursal","descripcion":null,"requerido":true},
             {"name":"fecha_desde","type":2,"label":"Desde","descripcion":null,"requerido":true},
             {"name":"watermark","type":3,"label":"Watermark","descripcion":null,"requerido":false}],
           "resultSchemaJson":"[{\"name\":\"nro\",\"dataType\":\"número\"}]"},
          {"id":2,"route":"/stock/movimientos/","httpMethod":"post","description":null,
           "dataSourceName":"Oracle - Produccion","isActive":true,
           "parameterDefinitions":[{"name":"deposito","type":1,"label":null,"requerido":true}],
           "resultSchemaJson":null},
          {"id":3,"route":"viejo/apagado","httpMethod":"GET","dataSourceName":"MySQL","isActive":false,
           "parameterDefinitions":[],"resultSchemaJson":null}
        ]
        """;

    private static Task<CatalogoResult> Listar(string cuerpo, HttpStatusCode codigo = HttpStatusCode.OK) =>
        new DataHubCatalogo(new Handler(cuerpo, codigo))
            .ListarAsync("http://srv-datahub:5000", "consuAdmin", "clave", default);

    [Fact]
    public async Task Mapea_los_tipos_de_parametro_que_vienen_como_numero()
    {
        var r = await Listar(Catalogo);

        Assert.True(r.Ok);
        var facturas = r.Endpoints!.Single(e => e.Ruta == "ventas/facturas");
        Assert.Equal("texto", facturas.Parametros.Single(p => p.Nombre == "sucursal").Tipo);
        Assert.Equal("fecha", facturas.Parametros.Single(p => p.Nombre == "fecha_desde").Tipo);
        Assert.Equal("fechaHora", facturas.Parametros.Single(p => p.Nombre == "watermark").Tipo);
        Assert.Equal("numero", r.Endpoints!.Single(e => e.Ruta == "stock/movimientos")
                                           .Parametros.Single().Tipo);
    }

    [Fact]
    public async Task Deja_afuera_los_endpoints_inactivos()
    {
        var r = await Listar(Catalogo);

        Assert.Equal(2, r.Endpoints!.Count);
        Assert.DoesNotContain(r.Endpoints!, e => e.Ruta == "viejo/apagado");
    }

    [Fact]
    public async Task Normaliza_la_ruta_y_el_metodo()
    {
        var r = await Listar(Catalogo);

        var stock = r.Endpoints!.Single(e => e.Ruta == "stock/movimientos"); // sin las barras
        Assert.Equal("POST", stock.MetodoHttp);                              // en mayusculas
    }

    [Fact]
    public async Task Distingue_los_que_tienen_esquema_guardado_de_los_que_no()
    {
        var r = await Listar(Catalogo);

        Assert.False(string.IsNullOrWhiteSpace(
            r.Endpoints!.Single(e => e.Ruta == "ventas/facturas").EsquemaJson));
        Assert.True(string.IsNullOrWhiteSpace(
            r.Endpoints!.Single(e => e.Ruta == "stock/movimientos").EsquemaJson));
    }

    [Fact]
    public async Task Usa_la_credencial_del_catalogo_por_Basic_contra_api_endpoints()
    {
        var handler = new Handler(Catalogo);
        await new DataHubCatalogo(handler)
            .ListarAsync("http://srv-datahub:5000/", "consuAdmin", "clave", default);

        Assert.Equal("http://srv-datahub:5000/api/endpoints", handler.Pedido!.RequestUri!.ToString());
        Assert.Equal("consuAdmin:clave", Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.Pedido.Headers.Authorization!.Parameter!)));
    }

    [Fact]
    public async Task Un_401_dice_que_hace_falta_un_administrador_de_DataHub()
    {
        var r = await Listar("no autorizado", HttpStatusCode.Unauthorized);

        Assert.False(r.Ok);
        Assert.Contains("administrador", r.Error);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_el_catalogo_no_revienta()
    {
        var r = await Listar("{\"mensaje\":\"hola\"}");

        // Un objeto en vez de un array: no es un error de transporte, pero no hay endpoints.
        Assert.True(r.Ok);
        Assert.Empty(r.Endpoints!);
    }

    [Fact]
    public async Task Un_json_roto_vuelve_como_error_y_no_como_excepcion()
    {
        var r = await Listar("{esto no es json");

        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public async Task Una_URL_vacia_se_corta_antes_de_salir_a_la_red()
    {
        var r = await new DataHubCatalogo().ListarAsync("", "u", "p", default);

        Assert.False(r.Ok);
        Assert.Contains("URL", r.Error);
    }
}
