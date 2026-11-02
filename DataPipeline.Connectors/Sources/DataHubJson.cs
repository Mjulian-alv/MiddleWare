// Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Origen DataHub CompLook: lectura en streaming de la
// respuesta de /api/query y conversion de valores segun el esquema declarado.
// DataHub materializa el resultado completo antes de responder (es su contrato, no lo cambiamos),
// pero de este lado NO se vuelve a materializar: el array "rows" se recorre con Utf8JsonReader sobre
// el stream y se emite un Record por elemento. Separado de DataHubSource para poder probarlo sin IO.
using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Sources;

/// <summary>Error devuelto por DataHub. Lleva el texto tal como vino, sin reemplazarlo.</summary>
public sealed class DataHubException : Exception
{
    public DataHubException(string mensaje) : base(mensaje) { }
}

/// <summary>Lee la respuesta de DataHub (<c>{success, columns, rows, data, error}</c>) en streaming.</summary>
public sealed class DataHubLector
{
    private JsonReaderState _estado = new(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
    private string? _prop;
    private bool _enRows;
    private int _profundidadRows = -1;

    /// <summary>Columnas informadas por DataHub, en orden. Vienen antes que las filas.</summary>
    public List<string> Columnas { get; } = new();

    /// <summary>Valor de <c>success</c>. Null si la respuesta no lo trajo.</summary>
    public bool? Success { get; private set; }

    /// <summary>Texto de <c>error</c>, tal como lo mando DataHub.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Recorre el stream y emite un Record por fila. Si DataHub respondio <c>success: false</c>
    /// —cosa que hace con HTTP 200— no emite nada y tira <see cref="DataHubException"/> con su texto.
    /// </summary>
    public async IAsyncEnumerable<Record> LeerFilasAsync(
        Stream stream,
        IReadOnlyDictionary<string, string> tiposPorColumna,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pipe = PipeReader.Create(stream);
        var fallo = false;

        while (true)
        {
            var resultado = await pipe.ReadAsync(ct);
            var buffer = resultado.Buffer;

            var filas = Avanzar(ref buffer, resultado.IsCompleted, tiposPorColumna,
                                out var consumido, out var examinado);

            // Un success:false corta la emision pero se sigue leyendo: "error" viene despues en el JSON.
            if (Success == false) fallo = true;

            if (!fallo)
                foreach (var fila in filas)
                    yield return fila;

            pipe.AdvanceTo(consumido, examinado);

            // Con final=true un JSON cortado hace que Utf8JsonReader tire JsonException desde Avanzar,
            // asi que aca no hace falta detectar truncamiento a mano.
            if (resultado.IsCompleted) break;
        }

        await pipe.CompleteAsync();

        // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Revision 1, hallazgo P2: antes solo fallaba con
        // success:false, asi que una respuesta SIN "success" —truncada, o de otro formato— pasaba como
        // corrida exitosa y sus filas entraban al pipeline. Se exige el true explicito: la unica
        // respuesta buena es la que DataHub marca como buena.
        if (Success != true)
            throw new DataHubException(
                Success is null
                    ? "La respuesta de DataHub no trae 'success'. No es una respuesta valida de /api/query."
                    : string.IsNullOrWhiteSpace(Error)
                        ? "DataHub respondio success:false sin texto de error."
                        : Error!);
    }

    /// <summary>Consume lo que haya completo en el buffer. Lo parcial queda para la proxima vuelta.</summary>
    private List<Record> Avanzar(
        ref ReadOnlySequence<byte> buffer,
        bool final,
        IReadOnlyDictionary<string, string> tipos,
        out SequencePosition consumido,
        out SequencePosition examinado)
    {
        var filas = new List<Record>();
        var rd = new Utf8JsonReader(buffer, final, _estado);
        var faltanDatos = false;

        while (!faltanDatos)
        {
            // Copia del lector ANTES de leer: si el token resulta ser una fila que todavia no llego
            // entera, se vuelve aca. Sin esto el StartObject queda consumido y la fila se pierde en
            // silencio, que es el peor final posible: menos filas y ningun error.
            var antes = rd;
            if (!rd.Read()) break;

            switch (rd.TokenType)
            {
                case JsonTokenType.PropertyName when rd.CurrentDepth == 1:
                    _prop = rd.GetString();
                    break;

                case JsonTokenType.StartArray when Es(_prop, "rows"):
                    _enRows = true;
                    _profundidadRows = rd.CurrentDepth;
                    break;

                case JsonTokenType.EndArray when _enRows && rd.CurrentDepth == _profundidadRows:
                    _enRows = false;
                    break;

                case JsonTokenType.StartObject when _enRows && rd.CurrentDepth == _profundidadRows + 1:
                    {
                        // El lector es un struct: la copia sirve para ver si la fila entera ya llego
                        // sin consumirla. Si falta, se corta y se reintenta con mas bytes.
                        var sonda = rd;
                        if (!sonda.TrySkip())
                        {
                            rd = antes;          // retroceder: la fila se reintenta entera
                            faltanDatos = true;
                            break;
                        }

                        using (var doc = JsonDocument.ParseValue(ref rd))
                            filas.Add(ARecord(doc.RootElement, tipos));
                        break;
                    }

                case JsonTokenType.True or JsonTokenType.False when Es(_prop, "success") && rd.CurrentDepth == 1:
                    Success = rd.GetBoolean();
                    break;

                case JsonTokenType.String when Es(_prop, "error") && rd.CurrentDepth == 1:
                    Error = rd.GetString();
                    break;

                case JsonTokenType.String when _enRows is false && Es(_prop, "columns") && rd.CurrentDepth == 2:
                    Columnas.Add(rd.GetString() ?? "");
                    break;
            }
        }

        _estado = rd.CurrentState;
        consumido = rd.Position;
        examinado = buffer.End;
        return filas;
    }

    private static bool Es(string? prop, string nombre) =>
        prop is not null && prop.Equals(nombre, StringComparison.OrdinalIgnoreCase);

    private static Record ARecord(JsonElement obj, IReadOnlyDictionary<string, string> tipos)
    {
        var valores = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            valores[p.Name] = Convertir(p.Value, tipos.GetValueOrDefault(p.Name));
        return new Record { Values = valores };
    }

    /// <summary>
    /// Convierte un valor JSON al CLR usando el tipo que DataHub declaro en su esquema.
    /// SIEMPRE con InvariantCulture: los valores viajan como texto y parsearlos con la cultura de la
    /// maquina es el mismo error que ya se corrigio en el commit 598464c y otra vez en la TAREA 00038.
    /// Si el valor no coincide con el tipo declarado se devuelve crudo, sin adivinar.
    /// </summary>
    public static object? Convertir(JsonElement el, string? tipoDeclarado)
    {
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        switch (tipoDeclarado?.ToLowerInvariant())
        {
            case "fecha":
                if (el.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(el.GetString(), CultureInfo.InvariantCulture,
                                         DateTimeStyles.None, out var fecha))
                    return fecha;
                break;

            // DataHub escribe "número" con acento; se acepta sin acento por las dudas.
            case "número":
            case "numero":
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var n))
                    return n;
                if (el.ValueKind == JsonValueKind.String
                    && decimal.TryParse(el.GetString(), NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out var n2))
                    return n2;
                break;

            case "booleano":
                if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return el.GetBoolean();
                if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b))
                    return b;
                break;

            case "texto":
                return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        }

        return Crudo(el);
    }

    private static object? Crudo(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => el.ToString()
    };

    /// <summary>
    /// Lee el <c>ResultSchemaJson</c> de DataHub (<c>[{name, dataType}]</c>) a un diccionario
    /// columna→tipo. Un JSON vacio, nulo o invalido devuelve un diccionario vacio: sin esquema los
    /// valores quedan crudos, que es peor que tenerlo pero no rompe la corrida.
    /// </summary>
    public static Dictionary<string, string> LeerEsquema(string? json)
    {
        var tipos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return tipos;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return tipos;

            foreach (var col in doc.RootElement.EnumerateArray())
            {
                if (col.ValueKind != JsonValueKind.Object) continue;
                var nombre = Prop(col, "name");
                var tipo = Prop(col, "dataType");
                if (!string.IsNullOrEmpty(nombre)) tipos[nombre] = tipo ?? "desconocido";
            }
        }
        catch (JsonException)
        {
            return tipos;
        }

        return tipos;
    }

    private static string? Prop(JsonElement obj, string nombre)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        return null;
    }
}
