// Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Origen DataHub CompLook.
// Lee un endpoint de DataHub como origen del pipeline. El transporte es HTTP; el parseo y la
// conversion de valores viven en DataHubJson, que se prueba sin IO.
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;

namespace DataPipeline.Connectors.Sources;

/// <summary>Origen que consume un endpoint de DataHub. Nunca materializa el resultado completo.</summary>
public sealed class DataHubSource : ISource
{
    private readonly string _baseUrl;
    private readonly string _ruta;
    private readonly string _metodo;
    private readonly IReadOnlyDictionary<string, string> _parametros;
    private readonly string? _esquemaJson;
    private readonly string _usuario;
    private readonly string _password;
    private readonly HttpMessageHandler? _handler;

    public DataHubSource(
        string baseUrl,
        string ruta,
        string metodoHttp,
        IReadOnlyDictionary<string, string> parametros,
        string? esquemaJson,
        string usuario,
        string password,
        HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl;
        _ruta = ruta;
        _metodo = string.IsNullOrWhiteSpace(metodoHttp) ? "GET" : metodoHttp.Trim().ToUpperInvariant();
        _parametros = parametros;
        _esquemaJson = esquemaJson;
        _usuario = usuario;
        _password = password;
        _handler = handler;
    }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(
        RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var tipos = DataHubLector.LeerEsquema(_esquemaJson);
        var valores = ResolverParametros(ctx);

        using var http = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan; // el corte lo maneja el CancellationToken de la corrida

        using var pedido = ArmarPedido(valores);
        pedido.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_usuario}:{_password}")));

        using var respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);

        // DataHub devuelve success:false CON HTTP 200, asi que mirar el status no alcanza (regla R6);
        // pero un 401/404 si llega como status y conviene traducirlo a algo que se entienda.
        if (!respuesta.IsSuccessStatusCode)
            throw new DataHubException(await DescribirFalloAsync(respuesta, ct));

        var lector = new DataHubLector();
        await using var cuerpo = await respuesta.Content.ReadAsStreamAsync(ct);

        // Se pasa la lista del lector, no una copia: "columns" se parsea antes que las filas, pero
        // RecordBatcher recibe la referencia al construirse, asi que todos los lotes ven la lista ya
        // completa. Si DataHub no informo columnas (solo las manda cuando hay al menos una fila), los
        // lotes quedan con la lista vacia y no hay filas que describir.
        await foreach (var lote in RecordBatcher.Batch(
                           lector.Columnas, lector.LeerFilasAsync(cuerpo, tipos, ct), ctx.BatchSize, ct))
            yield return lote;
    }

    /// <summary>
    /// Manda EXACTAMENTE los parametros que el endpoint declara, ni uno mas.
    /// DataHub arma el comando con Dapper agregando todos los que recibe, esten o no en el SQL, asi
    /// que un parametro de mas hace fallar la consulta (en Oracle, con un ORA-01036 que no orienta).
    /// Si el endpoint declara "watermark", se manda siempre: vacio en la primera corrida (regla R1).
    /// </summary>
    private Dictionary<string, string> ResolverParametros(RunContext ctx)
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nombre, fijo) in _parametros)
        {
            valores[nombre] = ctx.Parameters.TryGetValue(nombre, out var deCorrida) && deCorrida is not null
                ? deCorrida
                : fijo ?? "";
        }
        return valores;
    }

    private HttpRequestMessage ArmarPedido(Dictionary<string, string> valores)
    {
        var url = $"{_baseUrl.TrimEnd('/')}/api/query/{_ruta.Trim('/')}";

        if (_metodo == "POST")
        {
            var json = JsonSerializer.Serialize(new { parameters = valores });
            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        if (valores.Count > 0)
        {
            var qs = string.Join("&", valores.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            url += "?" + qs;
        }
        return new HttpRequestMessage(HttpMethod.Get, url);
    }

    private async Task<string> DescribirFalloAsync(HttpResponseMessage r, CancellationToken ct)
    {
        var cuerpo = "";
        try { cuerpo = await r.Content.ReadAsStringAsync(ct); } catch (HttpRequestException) { /* sin cuerpo */ }
        if (cuerpo.Length > 400) cuerpo = cuerpo[..400];

        return (int)r.StatusCode switch
        {
            401 => $"DataHub rechazo la credencial de datos (401) para '{_ruta}'. "
                   + "El usuario tiene que estar asignado a ese endpoint en DataHub. " + cuerpo,
            404 => $"DataHub no encontro el endpoint '{_ruta}' por {_metodo} (404). "
                   + "Suele ser el metodo HTTP equivocado: DataHub busca por ruta Y metodo. " + cuerpo,
            _ => $"DataHub respondio {(int)r.StatusCode} {r.ReasonPhrase} para '{_ruta}'. {cuerpo}"
        };
    }
}
