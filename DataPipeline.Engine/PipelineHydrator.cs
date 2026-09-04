using System.Text.Json;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Security;

namespace DataPipeline.Engine;

/// <summary>
/// Resuelve los pasos Origen que referencian un dataSourceId: carga el DataSource, desencripta su
/// connectionString e inyecta connectionString (texto plano) en la Configuracion del paso, ANTES de
/// que corra el SourceFactory. Pasos con config inline (sin dataSourceId) quedan intactos.
/// </summary>
public sealed class PipelineHydrator
{
    private readonly IDataSourceStore _origenes;
    private readonly IEncryptionService _encryption;

    public PipelineHydrator(IDataSourceStore origenes, IEncryptionService encryption)
    {
        _origenes = origenes;
        _encryption = encryption;
    }

    public async Task HydrateAsync(PipelineDefinition def, CancellationToken ct)
    {
        foreach (var paso in def.Pasos.Where(p => p.Tipo == TipoPaso.Origen))
        {
            if (!paso.Configuracion.TryGetValue("dataSourceId", out var idEl)
                || idEl.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idEl.GetString(), out var id))
                continue;

            var ds = await _origenes.GetByIdAsync(id, ct);
            if (ds is null) continue;

            // Fecha: 03/09/2026 - TAREA: 00015 - Linea: 1 - Origenes de archivo: la config del paso
            // sale del DataSource (no hay secreto que desencriptar, a diferencia de connectionString).
            if (ds.Tipo == TipoDataSource.SqlServer)
            {
                var cs = string.IsNullOrEmpty(ds.ConnectionString) ? "" : _encryption.Decrypt(ds.ConnectionString);
                paso.Configuracion["connectionString"] = JsonSerializer.SerializeToElement(cs);
                continue;
            }

            paso.Configuracion["rutaCarpeta"] = JsonSerializer.SerializeToElement(ds.RutaCarpeta ?? "");
            paso.Configuracion["patronArchivo"] = JsonSerializer.SerializeToElement(ds.PatronArchivo ?? "");
            if (ds.Tipo == TipoDataSource.Csv)
            {
                paso.Configuracion["separador"] = JsonSerializer.SerializeToElement(ds.Separador ?? ",");
                paso.Configuracion["encoding"] = JsonSerializer.SerializeToElement(ds.Encoding ?? "UTF-8");
                paso.Configuracion["tieneEncabezado"] = JsonSerializer.SerializeToElement(ds.TieneEncabezado);
            }
            else if (ds.Tipo == TipoDataSource.Xlsx)
            {
                paso.Configuracion["hojaExcel"] = JsonSerializer.SerializeToElement(ds.HojaExcel ?? "");
            }
        }
    }
}
