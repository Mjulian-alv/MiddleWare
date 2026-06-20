using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Microsoft.Data.Sqlite;

namespace DataPipeline.Connectors.Stores;

/// <summary>IDataStore sobre SQLite. Tablas de tipado dinámico (sin tipos declarados).</summary>
public sealed class SqliteDataStore : IDataStore
{
    private readonly string _connectionString;

    public SqliteDataStore(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string table, IReadOnlyList<string> columns, ModoEscritura mode, CancellationToken ct)
    {
        if (mode == ModoEscritura.Combinar)
            throw new NotSupportedException("ModoEscritura.Combinar (upsert) se implementa en Fase 4.");
        if (columns.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columns));

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        if (mode == ModoEscritura.Reemplazar)
        {
            await using var drop = con.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {Quote(table)};";
            await drop.ExecuteNonQueryAsync(ct);
        }

        var defs = string.Join(", ", columns.Select(Quote));
        await using var crear = con.CreateCommand();
        crear.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({defs});";
        await crear.ExecuteNonQueryAsync(ct);
    }

    public async Task WriteBatchAsync(string table, RecordBatch batch, CancellationToken ct)
    {
        if (batch.Rows.Count == 0) return;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await con.BeginTransactionAsync(ct);

        var cols = batch.Columns;
        var listaCols = string.Join(", ", cols.Select(Quote));
        var listaParams = string.Join(", ", cols.Select((_, i) => "$p" + i));

        await using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {Quote(table)} ({listaCols}) VALUES ({listaParams});";

        var parms = new SqliteParameter[cols.Count];
        for (int i = 0; i < cols.Count; i++)
        {
            parms[i] = cmd.CreateParameter();
            parms[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(parms[i]);
        }

        foreach (var fila in batch.Rows)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                fila.Values.TryGetValue(cols[i], out var valor);
                parms[i].Value = valor ?? DBNull.Value;
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} LIMIT $take OFFSET $skip;";
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "$take"; pTake.Value = pageSize; cmd.Parameters.Add(pTake);
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "$skip"; pSkip.Value = (page - 1) * pageSize; cmd.Parameters.Add(pSkip);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();
        var filas = new List<Record>();
        while (await rd.ReadAsync(ct))
        {
            var vals = new Dictionary<string, object?>(cols.Length);
            for (int i = 0; i < cols.Length; i++)
                vals[cols[i]] = await rd.IsDBNullAsync(i, ct) ? null : rd.GetValue(i);
            filas.Add(new Record { Values = vals });
        }
        return new RecordBatch { Columns = cols, Rows = filas };
    }

    private static string Quote(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
