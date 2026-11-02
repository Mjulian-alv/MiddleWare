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
            //
            // Fecha: 31/10/2026 - TAREA: 00154 - Linea: 1 - Esto era "if SqlServer ... else archivo".
            // Con un tercer tipo esa forma binaria mandaba DataHub por la rama de archivo y le inyectaba
            // rutaCarpeta, fallando recien en la corrida y con un error que no hablaba del problema.
            // Ahora es un switch exhaustivo: un tipo nuevo que no este contemplado no compila.
            switch (ds.Tipo)
            {
                case TipoDataSource.SqlServer:
                    var cs = string.IsNullOrEmpty(ds.ConnectionString) ? "" : _encryption.Decrypt(ds.ConnectionString);
                    paso.Configuracion["connectionString"] = JsonSerializer.SerializeToElement(cs);
                    break;

                case TipoDataSource.DataHubCompLook:
                    // Solo la credencial DE DATOS. La del catalogo es de diseno: no se usa al correr.
                    paso.Configuracion["baseUrl"] = JsonSerializer.SerializeToElement(ds.BaseUrl ?? "");
                    paso.Configuracion["usuario"] = JsonSerializer.SerializeToElement(
                        string.IsNullOrEmpty(ds.Username) ? "" : _encryption.Decrypt(ds.Username));
                    paso.Configuracion["password"] = JsonSerializer.SerializeToElement(
                        string.IsNullOrEmpty(ds.Password) ? "" : _encryption.Decrypt(ds.Password));
                    break;

                case TipoDataSource.Csv:
                    paso.Configuracion["rutaCarpeta"] = JsonSerializer.SerializeToElement(ds.RutaCarpeta ?? "");
                    paso.Configuracion["patronArchivo"] = JsonSerializer.SerializeToElement(ds.PatronArchivo ?? "");
                    paso.Configuracion["separador"] = JsonSerializer.SerializeToElement(ds.Separador ?? ",");
                    paso.Configuracion["encoding"] = JsonSerializer.SerializeToElement(ds.Encoding ?? "UTF-8");
                    paso.Configuracion["tieneEncabezado"] = JsonSerializer.SerializeToElement(ds.TieneEncabezado);
                    break;

                case TipoDataSource.Xlsx:
                    paso.Configuracion["rutaCarpeta"] = JsonSerializer.SerializeToElement(ds.RutaCarpeta ?? "");
                    paso.Configuracion["patronArchivo"] = JsonSerializer.SerializeToElement(ds.PatronArchivo ?? "");
                    paso.Configuracion["hojaExcel"] = JsonSerializer.SerializeToElement(ds.HojaExcel ?? "");
                    break;

                default:
                    throw new NotSupportedException(
                        $"El origen '{ds.Nombre}' es de un tipo que el hydrator no contempla: {ds.Tipo}.");
            }
        }
    }
}
