using System.Data;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>
/// Descubre columnas via CommandBehavior.SchemaOnly (no baja datos). Bindea @watermark=NULL si el SQL
/// lo referencia (misma guarda que SqlServerSource). Cualquier excepcion -> Ok=false con el mensaje.
/// </summary>
public sealed class SqlServerSchemaDiscoverer : ISchemaDiscoverer
{
    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - El esquema de un endpoint sale del catalogo.
    private readonly IDataHubCatalogo _catalogo;

    public SqlServerSchemaDiscoverer(IDataHubCatalogo? catalogo = null) =>
        _catalogo = catalogo ?? new DataHubCatalogo();


    public async Task<SchemaResult> DescubrirSqlServerAsync(string connectionString, string sql, CancellationToken ct)
    {
        try
        {
            await using var con = new SqlConnection(connectionString);
            await con.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 0 };
            if (sql.Contains("@watermark", StringComparison.OrdinalIgnoreCase))
                cmd.Parameters.AddWithValue("@watermark", DBNull.Value);

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
            var cols = new List<ColumnaOrigen>(rd.FieldCount);
            for (int i = 0; i < rd.FieldCount; i++)
                cols.Add(new ColumnaOrigen { Nombre = rd.GetName(i), Tipo = rd.GetDataTypeName(i) });

            return new SchemaResult(true, cols, null);
        }
        catch (Exception ex)
        {
            return new SchemaResult(false, null, ex.Message);
        }
    }

    // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origenes de archivo: abre el archivo mas nuevo
    // sin procesar y lee su encabezado (no hace falta nada del cliente, a diferencia del SQL que el
    // usuario esta escribiendo en el editor).
    public Task<SchemaResult> DescubrirArchivoAsync(DataSource ds, CancellationToken ct)
    {
        try
        {
            FileSourceBase fuente = ds.Tipo switch
            {
                TipoDataSource.Csv => new CsvSource(ds.RutaCarpeta ?? "", ds.PatronArchivo, null, null,
                    ds.Separador, ds.Encoding, ds.TieneEncabezado),
                TipoDataSource.Xlsx => new XlsxSource(ds.RutaCarpeta ?? "", ds.PatronArchivo, null, null, ds.HojaExcel),
                _ => throw new NotSupportedException($"Tipo no soportado para descubrir archivo: '{ds.Tipo}'.")
            };

            var archivo = fuente.ArchivoMasNuevoSinProcesar();
            if (archivo is null)
                return Task.FromResult(new SchemaResult(false, null,
                    "No hay archivos sin procesar en la carpeta que matcheen el patron."));

            var columnas = fuente.LeerEncabezado(archivo)
                .Select(n => new ColumnaOrigen { Nombre = n, Tipo = "string" })
                .ToList();
            return Task.FromResult(new SchemaResult(true, columnas, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SchemaResult(false, null, ex.Message));
        }
    }

    // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Columnas de un endpoint de DataHub (decision D3).
    // El camino barato es leer el ResultSchemaJson que DataHub ya guardo al probar el endpoint: no
    // ejecuta nada, no espera, no toca Oracle y no exige tener cargados los parametros obligatorios.
    public async Task<SchemaResult> DescubrirDataHubAsync(
        OrigenDataHub origen,
        string ruta,
        string? metodoHttp,
        IReadOnlyDictionary<string, string>? parametros,
        bool ejecutarSiFaltaEsquema,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruta))
            return new SchemaResult(false, null, "No se eligio ningun endpoint.");

        var cat = await _catalogo.ListarAsync(origen.BaseUrl, origen.UsuarioCatalogo, origen.PasswordCatalogo, ct);
        if (!cat.Ok)
            return new SchemaResult(false, null, cat.Error);

        // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 3 - Revision 1, hallazgo P1: se busca por ruta Y
        // metodo. La misma ruta puede estar publicada como GET y como POST, con esquemas distintos.
        var candidatos = (cat.Endpoints ?? Array.Empty<EndpointDataHub>())
            .Where(e => e.Ruta.Equals(ruta.Trim('/'), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ep = string.IsNullOrWhiteSpace(metodoHttp)
            ? (candidatos.Count == 1 ? candidatos[0] : null)
            : candidatos.FirstOrDefault(e => e.MetodoHttp.Equals(metodoHttp.Trim(), StringComparison.OrdinalIgnoreCase));

        if (ep is null)
            return new SchemaResult(false, null,
                candidatos.Count > 1 && string.IsNullOrWhiteSpace(metodoHttp)
                    ? $"La ruta '{ruta}' esta publicada con mas de un metodo en DataHub "
                      + $"({string.Join(", ", candidatos.Select(c => c.MetodoHttp))}). Hay que elegir cual."
                    : $"El endpoint '{(string.IsNullOrWhiteSpace(metodoHttp) ? "" : metodoHttp + " ")}{ruta}' "
                      + "no esta en el catalogo de DataHub, o quedo inactivo.");

        var columnas = ColumnasDe(ep.EsquemaJson);
        if (columnas.Count > 0)
            return new SchemaResult(true, columnas, null);

        if (!ejecutarSiFaltaEsquema)
            return new SchemaResult(false, null,
                $"El endpoint '{ep.Ruta}' todavia no paso una prueba en DataHub, asi que no tiene "
                + "esquema guardado. Lo barato es probarlo una vez en DataHub: queda guardado y "
                + "sirve para todos los pipelines. Si no, se puede ejecutar la consulta desde aca.");

        return await EjecutandoAsync(origen, ep, parametros, ct);
    }

    /// <summary>Ultimo recurso: corre el endpoint de verdad y toma las columnas del primer lote.</summary>
    private static async Task<SchemaResult> EjecutandoAsync(
        OrigenDataHub origen,
        EndpointDataHub ep,
        IReadOnlyDictionary<string, string>? parametros,
        CancellationToken ct)
    {
        // Se mandan los parametros DECLARADOS, con el valor cargado si lo hay (regla R1): uno de mas
        // haria fallar la consulta del lado de DataHub.
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in ep.Parametros)
            valores[p.Nombre] = parametros is not null && parametros.TryGetValue(p.Nombre, out var v) ? v : "";

        var faltantes = ep.Parametros
            .Where(p => p.Requerido && !p.Nombre.Equals("watermark", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(valores[p.Nombre]))
            .Select(p => p.Nombre)
            .ToList();
        if (faltantes.Count > 0)
            return new SchemaResult(false, null,
                $"Para ejecutar '{ep.Ruta}' hay que cargar antes: {string.Join(", ", faltantes)}.");

        try
        {
            var fuente = new DataHubSource(origen.BaseUrl, ep.Ruta, ep.MetodoHttp, valores,
                                           ep.EsquemaJson, origen.UsuarioDatos, origen.PasswordDatos);
            var ctx = new RunContext
            {
                PipelineId = Guid.Empty,
                RunId = Guid.Empty,
                Parameters = new Dictionary<string, string>(),
                Encryption = new NoEncryption(),
                BatchSize = 1          // alcanza la primera fila: solo se quieren los nombres
            };

            await foreach (var lote in fuente.ReadAsync(ctx, ct))
            {
                var nombres = lote.Columns.Count > 0
                    ? lote.Columns
                    : lote.Rows.FirstOrDefault()?.Values.Keys.ToList() ?? new List<string>();

                return new SchemaResult(true,
                    nombres.Select(n => new ColumnaOrigen { Nombre = n, Tipo = "desconocido" }).ToList(),
                    null);
            }

            return new SchemaResult(false, null,
                $"'{ep.Ruta}' no devolvio ninguna fila, asi que DataHub no informo columnas. "
                + "Con los parametros actuales no se puede deducir el esquema.");
        }
        catch (DataHubException ex)
        {
            return new SchemaResult(false, null, ex.Message);
        }
    }

    private static List<ColumnaOrigen> ColumnasDe(string? esquemaJson) =>
        DataHubLector.LeerEsquema(esquemaJson)
            .Select(p => new ColumnaOrigen { Nombre = p.Key, Tipo = p.Value })
            .ToList();

    /// <summary>DataHubSource no desencripta nada: recibe las credenciales ya en claro.</summary>
    private sealed class NoEncryption : IEncryptionService
    {
        public string Encrypt(string text) => text;
        public string Decrypt(string encryptedText) => encryptedText;
    }
}
