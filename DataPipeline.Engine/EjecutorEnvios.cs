// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: el barrido de envios.
// El pipeline no ejecuta el envio: solo prende Pendiente. Quien ejecuta es siempre este barrido,
// llamado desde el mismo tick de 60s que ya corre los pipelines. Por eso el disparo y el reintento
// son la misma operacion, y no dos caminos que haya que mantener sincronizados.
//
// El cursor (UltimoCreado) avanza SOLO al final del envio completo, nunca por lote: si se cortara a
// la mitad, las filas del ultimo lote quedarian marcadas como enviadas sin haber llegado. Entre lotes
// se avanza por keyset (_creado, _key) y no por pagina: ver el comentario en IDataStore.LeerDesdeAsync.
using System.Globalization;
using System.Text.Json;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Envios;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

public sealed class EjecutorEnvios
{
    private readonly int _filasPorLote;
    private readonly IEnvioStore _envios;
    private readonly IPipelineStore _pipelines;
    private readonly IDataSourceStore _origenes;
    private readonly IDataStore _data;
    private readonly IHistorialCorridas _historial;
    private readonly IDestinoFactory _destinos;
    private readonly IEncryptionService _encryption;

    public EjecutorEnvios(IEnvioStore envios, IPipelineStore pipelines, IDataSourceStore origenes,
        IDataStore data, IHistorialCorridas historial, IDestinoFactory destinos, IEncryptionService encryption,
        int filasPorLote = 5_000)
    {
        // Configurable para que los tests puedan recorrer varios lotes con pocas filas: con el valor de
        // produccion, la rama que pide el lote siguiente por keyset no se ejercitaria nunca.
        _filasPorLote = filasPorLote > 0 ? filasPorLote : 5_000;
        _envios = envios;
        _pipelines = pipelines;
        _origenes = origenes;
        _data = data;
        _historial = historial;
        _destinos = destinos;
        _encryption = encryption;
    }

    /// <summary>Procesa los envios pendientes. Devuelve cuantos tomo (con exito o no).</summary>
    public async Task<int> ProcesarPendientesAsync(DateTime ahora, CancellationToken ct)
    {
        var pendientes = await _envios.TomarPendientesAsync(ahora, ct);
        var procesados = 0;

        foreach (var envio in pendientes)
        {
            try
            {
                await ProcesarUnoAsync(envio, ahora, ct);
            }
            catch (Exception ex)
            {
                // envio.IntentosFallidos es la foto de antes de este fallo: justo lo que pide el backoff.
                var (proximo, deshabilitar) = BackoffEnvios.Calcular(envio.IntentosFallidos, ahora);
                await _envios.RegistrarFalloAsync(envio.Id, ex.Message, proximo, deshabilitar, ct);
            }
            procesados++;
        }

        return procesados;
    }

    private async Task ProcesarUnoAsync(Envio envio, DateTime ahora, CancellationToken ct)
    {
        var pipeline = await _pipelines.GetByIdAsync(envio.PipelineId, ct)
            ?? throw new InvalidOperationException($"El envio '{envio.Nombre}' apunta a un pipeline que no existe.");
        var origen = await _origenes.GetByIdAsync(envio.DataSourceId, ct)
            ?? throw new InvalidOperationException($"El envio '{envio.Nombre}' apunta a un origen que no existe.");

        // Se lee el primer lote antes de abrir la conexion remota: sin filas nuevas no hay para que
        // molestar al servidor destino, ni ensuciar el historial con una corrida vacia.
        var lote = await _data.LeerDesdeAsync(pipeline.TablaDestino, envio.UltimoCreado, null, _filasPorLote, ct);
        if (lote.Rows.Count == 0)
        {
            await _envios.RegistrarExitoAsync(envio.Id, envio.UltimoCreado, ahora, ct);
            return;
        }

        var runId = await _historial.IniciarEnvioAsync(envio.PipelineId, envio.Id, ct);
        try
        {
            var destino = _destinos.Crear(origen, _encryption);
            // Fecha: 02/11/2026 - TAREA: 00158 - Linea: 1 - Las fechas se deciden por lo declarado en el pipeline, no por lo que devuelve SQLite.
            var fechas = ColumnasFecha(pipeline);
            await destino.EnsureTableAsync(envio.TablaDestino, ColumnasDe(lote, fechas), ct);

            long escritas = 0;
            DateTime? maximo = envio.UltimoCreado;

            while (lote.Rows.Count > 0)
            {
                var normalizado = Normalizar(lote, fechas);
                escritas += await destino.WriteBatchAsync(envio.TablaDestino, normalizado, pipeline.ColumnasClave, ct);

                var ultima = normalizado.Rows[^1];
                var creado = (DateTime)ultima.Values[ServingColumns.Creado]!;
                if (maximo is null || creado > maximo) maximo = creado;

                if (lote.Rows.Count < _filasPorLote) break;
                lote = await _data.LeerDesdeAsync(pipeline.TablaDestino, creado,
                    (string?)ultima.Values[ServingColumns.Key], _filasPorLote, ct);
            }

            await _historial.CompletarAsync(runId, true, escritas, escritas, null, ct);
            await _envios.RegistrarExitoAsync(envio.Id, maximo, ahora, ct);
        }
        catch (Exception ex)
        {
            await _historial.CompletarAsync(runId, false, 0, 0,
                $"Intento {envio.IntentosFallidos + 1}/{Envio.MaxIntentos}. {ex.Message}", ct);
            throw;
        }
    }

    /// <summary>
    /// Tipo de cada columna de negocio, tomado del primer valor no nulo del primer lote (decision D2
    /// del plan). _key y _creado no van: el destino las agrega con su tipo fijo. _procesado tampoco:
    /// es el ack de nuestros consumidores PULL, del otro lado no significa nada.
    /// </summary>
    // Fecha: 02/11/2026 - TAREA: 00158 - Linea: 1 - Las columnas de fecha salian NVARCHAR(MAX).
    // SQLite no tiene tipo fecha: guarda el DateTime como texto y lo devuelve como string, y el tipo
    // se inferia de ese string. Ahora las columnas declaradas como fecha se crean DATETIME2 aunque el
    // primer valor llegue como texto (o todos sean nulos en el primer lote).
    private static List<ColumnaDestino> ColumnasDe(RecordBatch lote, ISet<string> fechas) =>
        lote.Columns
            .Where(c => c != ServingColumns.Procesado && c != ServingColumns.Key && c != ServingColumns.Creado)
            .Select(c => new ColumnaDestino(c, fechas.Contains(c)
                ? typeof(DateTime)
                : lote.Rows.Select(f => f.Values.TryGetValue(c, out var v) ? v : null)
                    .FirstOrDefault(v => v is not null)?.GetType()))
            .ToList();

    private static readonly HashSet<string> TiposFecha = new(StringComparer.OrdinalIgnoreCase)
    {
        "fecha", "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset"
    };

    /// <summary>
    /// Columnas que el pipeline declara como fecha, con el nombre que tienen en el serving. Parte del
    /// esquema del origen y aplica en orden los renombres y casts de cada ColumnMapTransform (el cast
    /// gana: "date" la marca, cualquier otro tipo la desmarca). Las columnas calculadas no se infieren.
    /// </summary>
    public static HashSet<string> ColumnasFecha(PipelineDefinition pipeline)
    {
        var fechas = new HashSet<string>(
            pipeline.EsquemaOrigen.Where(c => TiposFecha.Contains(c.Tipo)).Select(c => c.Nombre),
            StringComparer.OrdinalIgnoreCase);

        foreach (var paso in pipeline.Pasos.Where(p => p.Tipo == TipoPaso.Transformacion && p.Conector == "ColumnMapTransform"))
        {
            foreach (var (viejo, nuevo) in LeerMapa(paso, "rename"))
                if (fechas.Remove(viejo)) fechas.Add(nuevo);
            foreach (var (col, tipo) in LeerMapa(paso, "cast"))
            {
                if (string.Equals(tipo, "date", StringComparison.OrdinalIgnoreCase)) fechas.Add(col);
                else fechas.Remove(col);
            }
        }
        return fechas;
    }

    private static Dictionary<string, string> LeerMapa(StepDefinition paso, string clave)
    {
        var mapa = new Dictionary<string, string>();
        if (paso.Configuracion.TryGetValue(clave, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var p in v.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) mapa[p.Name] = p.Value.GetString()!;
        return mapa;
    }

    /// <summary>
    /// Saca _procesado y convierte _creado y las columnas de fecha a DateTime. SQLite los devuelve como
    /// texto; mandarlos crudos dejaria la conversion a DATETIME2 en manos de la cultura del servidor remoto.
    /// </summary>
    private static RecordBatch Normalizar(RecordBatch lote, ISet<string> fechas) => new()
    {
        Columns = lote.Columns.Where(c => c != ServingColumns.Procesado).ToArray(),
        Rows = lote.Rows.Select(f => new Record
        {
            Values = f.Values
                .Where(kv => kv.Key != ServingColumns.Procesado)
                .ToDictionary(kv => kv.Key, kv =>
                    kv.Key == ServingColumns.Creado ? ComoFecha(kv.Value)
                    : fechas.Contains(kv.Key) ? FechaDeColumna(kv.Key, kv.Value)
                    : kv.Value)
        }).ToList()
    };

    private static object? FechaDeColumna(string columna, object? valor) => valor switch
    {
        null => null,
        DateTime or DateTimeOffset => valor,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        _ => throw new InvalidOperationException(
            $"La columna '{columna}' esta declarada como fecha pero tiene el valor '{valor}', que no se puede leer como fecha.")
    };

    private static object ComoFecha(object? valor) => valor switch
    {
        DateTime d => d,
        string s => DateTime.Parse(s, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"_creado con un valor inesperado: '{valor}'.")
    };
}
