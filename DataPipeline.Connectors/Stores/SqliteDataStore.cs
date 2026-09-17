using System.Globalization;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Pipelines;
using DataPipeline.Core.Records;
using Microsoft.Data.Sqlite;

namespace DataPipeline.Connectors.Stores;

/// <summary>
/// IDataStore sobre SQLite con ciclo de procesado. Cada fila lleva _key/_creado/_procesado.
/// Agregar = append + limpieza de _procesado=1; Reemplazar = upsert por _key (sin borrar ausentes).
/// </summary>
public sealed class SqliteDataStore : IDataStore
{
    private const char SeparadorClave = ''; // unit separator: improbable en datos

    private readonly string _connectionString;
    public SqliteDataStore(string connectionString) => _connectionString = connectionString;

    public async Task EnsureTableAsync(string table, IReadOnlyList<string> columnasNegocio, ModoEscritura mode, CancellationToken ct)
    {
        if (mode == ModoEscritura.Combinar)
            throw new NotSupportedException("ModoEscritura.Combinar no aplica al serving con ciclo de procesado.");
        if (columnasNegocio.Count == 0)
            throw new ArgumentException("No se puede crear una tabla sin columnas.", nameof(columnasNegocio));

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var todas = columnasNegocio
            .Concat(new[] { ServingColumns.Key, ServingColumns.Creado, ServingColumns.Procesado })
            .Select(Quote);
        await using (var crear = con.CreateCommand())
        {
            crear.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({string.Join(", ", todas)});";
            await crear.ExecuteNonQueryAsync(ct);
        }

        if (mode == ModoEscritura.Reemplazar)
        {
            await using var idx = con.CreateCommand();
            idx.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {Quote("ux_" + table + "_key")} " +
                              $"ON {Quote(table)} ({Quote(ServingColumns.Key)});";
            await idx.ExecuteNonQueryAsync(ct);
        }
        else // Agregar: limpieza de filas ya procesadas
        {
            await using var del = con.CreateCommand();
            del.CommandText = $"DELETE FROM {Quote(table)} WHERE {Quote(ServingColumns.Procesado)} = 1;";
            await del.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task WriteBatchAsync(string table, RecordBatch batch, IReadOnlyList<string> columnasClave, ModoEscritura mode, CancellationToken ct)
    {
        if (batch.Rows.Count == 0) return;
        if (mode == ModoEscritura.Reemplazar && columnasClave.Count == 0)
            throw new InvalidOperationException("Reemplazar (upsert) requiere al menos una columna clave.");

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await con.BeginTransactionAsync(ct);

        var negocio = batch.Columns;
        var todas = negocio.Concat(new[] { ServingColumns.Key, ServingColumns.Creado, ServingColumns.Procesado }).ToArray();
        var listaCols = string.Join(", ", todas.Select(Quote));
        var listaParams = string.Join(", ", todas.Select((_, i) => "$p" + i));

        var sql = $"INSERT INTO {Quote(table)} ({listaCols}) VALUES ({listaParams})";
        if (mode == ModoEscritura.Reemplazar)
        {
            var sets = negocio.Select(c => $"{Quote(c)} = excluded.{Quote(c)}")
                .Concat(new[]
                {
                    $"{Quote(ServingColumns.Creado)} = excluded.{Quote(ServingColumns.Creado)}",
                    $"{Quote(ServingColumns.Procesado)} = 0"
                });
            sql += $" ON CONFLICT({Quote(ServingColumns.Key)}) DO UPDATE SET {string.Join(", ", sets)}";
        }
        sql += ";";

        await using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var parms = new SqliteParameter[todas.Length];
        for (int i = 0; i < todas.Length; i++)
        {
            parms[i] = cmd.CreateParameter();
            parms[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(parms[i]);
        }

        int idxKey = negocio.Count, idxCreado = negocio.Count + 1, idxProc = negocio.Count + 2;
        var ahora = DateTime.Now;
        foreach (var fila in batch.Rows)
        {
            for (int i = 0; i < negocio.Count; i++)
            {
                fila.Values.TryGetValue(negocio[i], out var valor);
                parms[i].Value = valor ?? DBNull.Value;
            }
            parms[idxKey].Value = CalcularKey(fila, columnasClave);
            parms[idxCreado].Value = ahora;
            parms[idxProc].Value = 0;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<RecordBatch> ReadPagedAsync(string table, int page, int pageSize, bool incluirProcesados, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var filtro = incluirProcesados ? "" : $" WHERE {Quote(ServingColumns.Procesado)} = 0";
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)}{filtro} ORDER BY {Quote(ServingColumns.Creado)} " +
                          "LIMIT $take OFFSET $skip;";
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

    // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: lectura por cursor, paginada
    // por keyset. Orden por (_creado, _key) y no solo por _creado: todas las filas de un mismo lote
    // comparten el _creado exacto (WriteBatchAsync toma DateTime.Now una sola vez), asi que _key es lo
    // unico que las ordena. Keyset y no OFFSET: ver el comentario en IDataStore.
    public async Task<RecordBatch> LeerDesdeAsync(string table, DateTime? creadoDesde, string? keyDesde, int limite, CancellationToken ct)
    {
        if (limite <= 0) limite = 100;

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var creado = Quote(ServingColumns.Creado);
        var key = Quote(ServingColumns.Key);
        var filtro = (creadoDesde, keyDesde) switch
        {
            (null, _) => "",
            (_, null) => $" WHERE {creado} > $desde",
            _ => $" WHERE {creado} > $desde OR ({creado} = $desde AND {key} > $key)"
        };

        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)}{filtro} ORDER BY {creado}, {key} LIMIT $take;";
        if (creadoDesde is not null)
        {
            var pDesde = cmd.CreateParameter(); pDesde.ParameterName = "$desde"; pDesde.Value = creadoDesde.Value; cmd.Parameters.Add(pDesde);
        }
        if (creadoDesde is not null && keyDesde is not null)
        {
            var pKey = cmd.CreateParameter(); pKey.ParameterName = "$key"; pKey.Value = keyDesde; cmd.Parameters.Add(pKey);
        }
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "$take"; pTake.Value = limite; cmd.Parameters.Add(pTake);

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

    public async Task<int> MarcarProcesadasAsync(string table, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return 0;
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        var ph = string.Join(", ", keys.Select((_, i) => "$k" + i));
        cmd.CommandText = $"UPDATE {Quote(table)} SET {Quote(ServingColumns.Procesado)} = 1 " +
                          $"WHERE {Quote(ServingColumns.Key)} IN ({ph});";
        for (int i = 0; i < keys.Count; i++)
        {
            var p = cmd.CreateParameter(); p.ParameterName = "$k" + i; p.Value = keys[i]; cmd.Parameters.Add(p);
        }
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string CalcularKey(Record fila, IReadOnlyList<string> claves)
    {
        if (claves.Count == 0) return Guid.NewGuid().ToString("N");
        var partes = claves.Select(c => fila.Values.TryGetValue(c, out var v)
            ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "");
        return string.Join(SeparadorClave, partes);
    }

    private static string Quote(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
