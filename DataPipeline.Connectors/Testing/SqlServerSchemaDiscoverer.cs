using System.Data;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.DataSources;
using DataPipeline.Core.Pipelines;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Testing;

/// <summary>
/// Descubre columnas via CommandBehavior.SchemaOnly (no baja datos). Bindea @watermark=NULL si el SQL
/// lo referencia (misma guarda que SqlServerSource). Cualquier excepcion -> Ok=false con el mensaje.
/// </summary>
public sealed class SqlServerSchemaDiscoverer : ISchemaDiscoverer
{
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
}
