// Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Revision 1, hallazgo P1.
// DataHub busca sus endpoints por ruta Y metodo, asi que la misma ruta puede estar publicada dos
// veces, como GET y como POST, con esquemas distintos. Buscar por ruta sola devolvia cualquiera de
// los dos: el paso quedaba con las columnas del otro endpoint y nadie se enteraba hasta correrlo.
using DataPipeline.Connectors.Testing;
using DataPipeline.Core.Abstractions;
using Xunit;

namespace DataPipeline.Tests;

public class DataHubEsquemaPorMetodoTests
{
    private sealed class CatalogoFalso : IDataHubCatalogo
    {
        private readonly List<EndpointDataHub> _eps;
        public CatalogoFalso(params EndpointDataHub[] eps) => _eps = eps.ToList();

        public Task<CatalogoResult> ListarAsync(string b, string u, string p, CancellationToken ct) =>
            Task.FromResult(new CatalogoResult(true, _eps, null));
    }

    private static EndpointDataHub Ep(string ruta, string metodo, string columna) =>
        new(ruta, metodo, null, "Oracle", new List<ParametroEndpoint>(),
            $$"""[{"name":"{{columna}}","dataType":"texto"}]""");

    private static readonly OrigenDataHub Origen =
        new("http://dh:5000", "consuAdmin", "c", "dataPipeline", "d");

    [Fact]
    public async Task Elige_el_endpoint_por_ruta_Y_metodo()
    {
        var disc = new SqlServerSchemaDiscoverer(new CatalogoFalso(
            Ep("ventas/facturas", "GET", "columna_del_get"),
            Ep("ventas/facturas", "POST", "columna_del_post")));

        var get = await disc.DescubrirDataHubAsync(Origen, "ventas/facturas", "GET", null, false, default);
        var post = await disc.DescubrirDataHubAsync(Origen, "ventas/facturas", "POST", null, false, default);

        Assert.True(get.Ok);
        Assert.Equal("columna_del_get", get.Columnas!.Single().Nombre);
        Assert.True(post.Ok);
        Assert.Equal("columna_del_post", post.Columnas!.Single().Nombre);
    }

    [Fact]
    public async Task Sin_metodo_y_con_la_ruta_duplicada_no_adivina()
    {
        var disc = new SqlServerSchemaDiscoverer(new CatalogoFalso(
            Ep("ventas/facturas", "GET", "a"),
            Ep("ventas/facturas", "POST", "b")));

        var r = await disc.DescubrirDataHubAsync(Origen, "ventas/facturas", null, null, false, default);

        Assert.False(r.Ok);
        Assert.Contains("mas de un metodo", r.Error);
    }

    [Fact]
    public async Task Sin_metodo_y_con_una_sola_coincidencia_resuelve_igual()
    {
        var disc = new SqlServerSchemaDiscoverer(new CatalogoFalso(
            Ep("ventas/facturas", "GET", "nro_factura")));

        var r = await disc.DescubrirDataHubAsync(Origen, "ventas/facturas", null, null, false, default);

        Assert.True(r.Ok);
        Assert.Equal("nro_factura", r.Columnas!.Single().Nombre);
    }

    [Fact]
    public async Task El_metodo_equivocado_no_devuelve_el_esquema_del_otro()
    {
        var disc = new SqlServerSchemaDiscoverer(new CatalogoFalso(
            Ep("stock/movimientos", "POST", "id_mov")));

        var r = await disc.DescubrirDataHubAsync(Origen, "stock/movimientos", "GET", null, false, default);

        Assert.False(r.Ok);
        Assert.Contains("no esta en el catalogo", r.Error);
    }
}
