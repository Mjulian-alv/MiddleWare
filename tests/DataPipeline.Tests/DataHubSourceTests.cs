// Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Origen DataHub CompLook.
// Lo que se prueba aca, por orden de lo que duele si se rompe:
//   1. Una fila partida entre dos lecturas del stream NO se pierde (seria menos filas y cero errores).
//   2. success:false con HTTP 200 corta la corrida (regla R6): mirar el status no alcanza.
//   3. Los valores se convierten con InvariantCulture (regla R3), no con la cultura de la maquina.
//   4. Se mandan EXACTAMENTE los parametros declarados, ni uno de mas (si no, Oracle falla).
using System.Globalization;
using System.Net;
using System.Text;
using DataPipeline.Connectors.Security;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using Record = DataPipeline.Core.Records.Record;
using DataPipeline.Core.Runtime;
using Xunit;

namespace DataPipeline.Tests;

public class DataHubSourceTests
{
    private const string Esquema = """
        [{"name":"nro","dataType":"número"},
         {"name":"fecha","dataType":"fecha"},
         {"name":"cliente","dataType":"texto"},
         {"name":"pagado","dataType":"booleano"}]
        """;

    private static RunContext Ctx(Dictionary<string, string>? parametros = null, int lote = 5_000) => new()
    {
        PipelineId = Guid.NewGuid(),
        RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = lote
    };

    /// <summary>Stream que entrega de a poquitos bytes, para forzar el corte en cualquier lugar.</summary>
    private sealed class StreamTacano : Stream
    {
        private readonly byte[] _datos;
        private readonly int _porLectura;
        private int _pos;

        public StreamTacano(string texto, int porLectura)
        {
            _datos = Encoding.UTF8.GetBytes(texto);
            _porLectura = porLectura;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(_porLectura, count), _datos.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_datos, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _datos.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly HttpStatusCode _codigo;
        private readonly string _cuerpo;
        public HttpRequestMessage? Pedido { get; private set; }
        public string? CuerpoPedido { get; private set; }

        public HandlerFalso(string cuerpo, HttpStatusCode codigo = HttpStatusCode.OK)
        {
            _cuerpo = cuerpo;
            _codigo = codigo;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Pedido = request;
            if (request.Content is not null)
                CuerpoPedido = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_codigo)
            {
                Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json")
            };
        }
    }

    private static async Task<List<Record>> LeerTodo(DataHubSource s, RunContext ctx)
    {
        var filas = new List<Record>();
        await foreach (var lote in s.ReadAsync(ctx, CancellationToken.None))
            filas.AddRange(lote.Rows);
        return filas;
    }

    private static string RespuestaCon(int filas)
    {
        var rows = string.Join(",", Enumerable.Range(1, filas).Select(i =>
            $$"""{"nro":{{i}},"fecha":"2026-03-0{{(i % 9) + 1}}T14:30:00","cliente":"Cliente {{i}}","pagado":true}"""));
        return $$"""{"success":true,"columns":["nro","fecha","cliente","pagado"],"rows":[{{rows}}],"data":null,"error":null}""";
    }

    // ── 1. El caso que mas duele: la fila cortada entre dos lecturas ────────────────────────
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(64)]
    [InlineData(4096)]
    public async Task No_pierde_filas_aunque_el_stream_corte_en_cualquier_lado(int bytesPorLectura)
    {
        var json = RespuestaCon(25);
        var lector = new DataHubLector();
        var tipos = DataHubLector.LeerEsquema(Esquema);

        var filas = new List<Record>();
        await foreach (var f in lector.LeerFilasAsync(new StreamTacano(json, bytesPorLectura), tipos, default))
            filas.Add(f);

        Assert.Equal(25, filas.Count);
        Assert.Equal(4, lector.Columnas.Count);
        // Y no solo la cantidad: que cada fila este entera y en orden.
        for (int i = 0; i < 25; i++)
            Assert.Equal($"Cliente {i + 1}", filas[i].Values["cliente"]);
    }

    // ── 2. Regla R6: success:false viene con HTTP 200 ──────────────────────────────────────
    [Fact]
    public async Task Un_success_false_con_HTTP_200_corta_la_corrida_con_el_texto_de_DataHub()
    {
        const string json = """
            {"success":false,"columns":[],"rows":[],"data":null,
             "error":"Error Oracle: ORA-00942: la tabla o vista no existe"}
            """;
        var handler = new HandlerFalso(json); // 200 OK a proposito
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "u", "p", handler);

        var ex = await Assert.ThrowsAsync<DataHubException>(() => LeerTodo(origen, Ctx()));
        Assert.Contains("ORA-00942", ex.Message);
    }

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Revision 1, hallazgo P2: una respuesta SIN
    // "success" pasaba como corrida exitosa y sus filas entraban al pipeline. Tiene que cortar.
    [Fact]
    public async Task Una_respuesta_sin_success_no_se_toma_por_buena()
    {
        const string json = """
            {"columns":["nro"],"rows":[{"nro":1},{"nro":2}]}
            """;
        var lector = new DataHubLector();

        var ex = await Assert.ThrowsAsync<DataHubException>(async () =>
        {
            await foreach (var _ in lector.LeerFilasAsync(
                new StreamTacano(json, 16), new Dictionary<string, string>(), default)) { }
        });

        Assert.Contains("no trae 'success'", ex.Message);
    }

    [Fact]
    public async Task Un_success_false_sin_texto_igual_corta()
    {
        var lector = new DataHubLector();
        var ex = await Assert.ThrowsAsync<DataHubException>(async () =>
        {
            await foreach (var _ in lector.LeerFilasAsync(
                new StreamTacano("""{"success":false,"rows":[],"error":null}""", 8),
                new Dictionary<string, string>(), default)) { }
        });
        Assert.Contains("success:false", ex.Message);
    }

    // ── 3. Regla R3: InvariantCulture, no la cultura de la maquina ─────────────────────────
    [Fact]
    public async Task Convierte_los_valores_con_InvariantCulture_aunque_la_maquina_tenga_otra()
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            // En de-DE el punto es separador de miles: con la cultura de la maquina, 1234.56 daria 123456.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            const string json = """
                {"success":true,"columns":["nro","fecha"],
                 "rows":[{"nro":"1234.56","fecha":"2026-03-05T14:30:00"}]}
                """;
            var tipos = DataHubLector.LeerEsquema(
                """[{"name":"nro","dataType":"número"},{"name":"fecha","dataType":"fecha"}]""");

            var filas = new List<Record>();
            await foreach (var f in new DataHubLector().LeerFilasAsync(new StreamTacano(json, 16), tipos, default))
                filas.Add(f);

            Assert.Equal(1234.56m, Assert.IsType<decimal>(filas[0].Values["nro"]));
            Assert.Equal(new DateTime(2026, 3, 5, 14, 30, 0), Assert.IsType<DateTime>(filas[0].Values["fecha"]));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public async Task Una_columna_fuera_del_esquema_queda_cruda_y_no_se_adivina()
    {
        const string json = """
            {"success":true,"columns":["nro","extra"],"rows":[{"nro":7,"extra":"2026-03-05T14:30:00"}]}
            """;
        var tipos = DataHubLector.LeerEsquema("""[{"name":"nro","dataType":"número"}]""");

        var filas = new List<Record>();
        await foreach (var f in new DataHubLector().LeerFilasAsync(new StreamTacano(json, 32), tipos, default))
            filas.Add(f);

        Assert.Equal(7m, filas[0].Values["nro"]);
        // "extra" parece fecha, pero el esquema no lo dice: se deja el texto tal cual.
        Assert.Equal("2026-03-05T14:30:00", Assert.IsType<string>(filas[0].Values["extra"]));
    }

    [Fact]
    public async Task Un_resultado_vacio_no_devuelve_lotes_ni_falla()
    {
        const string json = """{"success":true,"columns":[],"rows":[],"data":null,"error":null}""";
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "u", "p", new HandlerFalso(json));

        Assert.Empty(await LeerTodo(origen, Ctx()));
    }

    [Fact]
    public async Task Un_null_en_una_columna_tipada_llega_como_null()
    {
        const string json = """
            {"success":true,"columns":["fecha"],"rows":[{"fecha":null}]}
            """;
        var tipos = DataHubLector.LeerEsquema("""[{"name":"fecha","dataType":"fecha"}]""");

        var filas = new List<Record>();
        await foreach (var f in new DataHubLector().LeerFilasAsync(new StreamTacano(json, 9), tipos, default))
            filas.Add(f);

        Assert.Null(filas[0].Values["fecha"]);
    }

    // ── 4. Parametros: ni uno de mas ───────────────────────────────────────────────────────
    [Fact]
    public async Task Manda_solo_los_parametros_declarados_y_el_watermark_vacio_en_la_primera_corrida()
    {
        var handler = new HandlerFalso(RespuestaCon(1));
        var declarados = new Dictionary<string, string>
        {
            ["sucursal"] = "001",
            ["watermark"] = ""      // declarado por el endpoint, sin valor fijo
        };
        var origen = new DataHubSource("http://dh:5000/", "/ventas/facturas", "GET",
            declarados, Esquema, "u", "p", handler);

        // ctx trae un parametro que el endpoint NO declara: no tiene que viajar.
        await LeerTodo(origen, Ctx(new Dictionary<string, string> { ["noDeclarado"] = "x" }));

        var url = handler.Pedido!.RequestUri!.ToString();
        Assert.StartsWith("http://dh:5000/api/query/ventas/facturas?", url);
        Assert.Contains("sucursal=001", url);
        Assert.Contains("watermark=", url);
        Assert.DoesNotContain("noDeclarado", url);
    }

    [Fact]
    public async Task El_watermark_de_la_corrida_pisa_al_valor_fijo()
    {
        var handler = new HandlerFalso(RespuestaCon(1));
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string> { ["watermark"] = "" }, Esquema, "u", "p", handler);

        await LeerTodo(origen, Ctx(new Dictionary<string, string> { ["watermark"] = "2026-03-05T14:30:00" }));

        Assert.Contains("watermark=2026-03-05T14%3A30%3A00", handler.Pedido!.RequestUri!.ToString());
    }

    // ── 5. Regla R5: el metodo sale del catalogo ───────────────────────────────────────────
    [Fact]
    public async Task En_POST_los_parametros_van_en_el_cuerpo_y_no_en_la_URL()
    {
        var handler = new HandlerFalso(RespuestaCon(1));
        var origen = new DataHubSource("http://dh:5000", "stock/movimientos", "POST",
            new Dictionary<string, string> { ["deposito"] = "D1" }, Esquema, "u", "p", handler);

        await LeerTodo(origen, Ctx());

        Assert.Equal(HttpMethod.Post, handler.Pedido!.Method);
        Assert.DoesNotContain("?", handler.Pedido.RequestUri!.ToString());
        Assert.Contains("\"deposito\":\"D1\"", handler.CuerpoPedido);
    }

    [Fact]
    public async Task Manda_la_credencial_de_datos_por_Basic()
    {
        var handler = new HandlerFalso(RespuestaCon(1));
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "dataPipeline", "secreta", handler);

        await LeerTodo(origen, Ctx());

        var auth = handler.Pedido!.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal("dataPipeline:secreta",
            Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    // ── 6. Los status que importan, con mensajes que orientan ──────────────────────────────
    [Fact]
    public async Task Un_401_explica_que_falta_la_asignacion_del_usuario_al_endpoint()
    {
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "u", "p",
            new HandlerFalso("Credenciales invalidas.", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<DataHubException>(() => LeerTodo(origen, Ctx()));
        Assert.Contains("401", ex.Message);
        Assert.Contains("asignado", ex.Message);
    }

    [Fact]
    public async Task Un_404_apunta_al_metodo_HTTP_que_es_la_causa_habitual()
    {
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "u", "p",
            new HandlerFalso("No se encontro un endpoint activo", HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<DataHubException>(() => LeerTodo(origen, Ctx()));
        Assert.Contains("404", ex.Message);
        Assert.Contains("metodo", ex.Message);
    }

    // ── 7. El esquema, que puede venir de cualquier manera ─────────────────────────────────
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es json")]
    [InlineData("{}")]
    public void Un_esquema_ausente_o_roto_no_rompe_nada(string? json)
    {
        Assert.Empty(DataHubLector.LeerEsquema(json));
    }

    [Fact]
    public void El_esquema_se_lee_sin_importar_el_casing_de_las_claves()
    {
        var tipos = DataHubLector.LeerEsquema("""[{"Name":"total","DataType":"número"}]""");
        Assert.Equal("número", tipos["total"]);
    }

    [Fact]
    public async Task Parte_en_lotes_del_tamano_pedido()
    {
        var origen = new DataHubSource("http://dh:5000", "ventas/facturas", "GET",
            new Dictionary<string, string>(), Esquema, "u", "p", new HandlerFalso(RespuestaCon(10)));

        var lotes = new List<int>();
        await foreach (var lote in origen.ReadAsync(Ctx(lote: 4), CancellationToken.None))
            lotes.Add(lote.Rows.Count);

        Assert.Equal(new[] { 4, 4, 2 }, lotes);
    }
}
