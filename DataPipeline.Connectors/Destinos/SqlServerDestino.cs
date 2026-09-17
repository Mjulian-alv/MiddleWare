// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: cascara fina de ADO.NET sobre
// SqlServerDestinoSql. Toda la logica de armado vive alla, testeada sin servidor; los nombres de
// parametro salen de SqlServerDestinoSql.Parametro para que el SQL y el binding no puedan divergir.
// El lote va en una transaccion: o entra completo o no entra, para que el cursor no avance sobre algo
// a medias.
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Destinos;

public sealed class SqlServerDestino : IDestinoDatos
{
    private readonly string _connectionString;
    public SqlServerDestino(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string tabla, IReadOnlyList<ColumnaDestino> columnas, CancellationToken ct)
    {
        if (columnas.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columnas));

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(SqlServerDestinoSql.CrearTabla(tabla, columnas), con);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> WriteBatchAsync(string tabla, RecordBatch lote, IReadOnlyList<string> columnasClave, CancellationToken ct)
    {
        if (lote.Rows.Count == 0) return 0;

        // Se escriben las columnas de negocio + _key + _creado. _procesado se descarta.
        var columnas = lote.Columns.Where(c => c != ServingColumns.Procesado).ToArray();
        var parametros = columnas.Select((c, i) => SqlServerDestinoSql.Parametro(c, i)).ToArray();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand(SqlServerDestinoSql.Upsert(tabla, columnas), con, tx);

        foreach (var p in parametros)
            cmd.Parameters.Add(new SqlParameter(p, DBNull.Value));

        long escritas = 0;
        foreach (var fila in lote.Rows)
        {
            for (int i = 0; i < columnas.Length; i++)
            {
                fila.Values.TryGetValue(columnas[i], out var valor);
                cmd.Parameters[parametros[i]].Value = valor ?? DBNull.Value;
            }
            await cmd.ExecuteNonQueryAsync(ct);
            escritas++;
        }

        await tx.CommitAsync(ct);
        return escritas;
    }
}
