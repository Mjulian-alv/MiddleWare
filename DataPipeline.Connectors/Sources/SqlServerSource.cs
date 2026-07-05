using System.Runtime.CompilerServices;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using Microsoft.Data.SqlClient;

namespace DataPipeline.Connectors.Sources;

/// <summary>Source de SQL Server en streaming (DbDataReader + lotes). Nunca materializa todo.</summary>
public sealed class SqlServerSource : ISource
{
    private readonly string _connectionStringRaw;
    private readonly string _sql;
    private readonly bool _encriptada;

    public SqlServerSource(string connectionString, string sql, bool connectionStringEncrypted)
    {
        _connectionStringRaw = connectionString;
        _sql = sql;
        _encriptada = connectionStringEncrypted;
    }

    public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var cs = _encriptada ? ctx.Encryption.Decrypt(_connectionStringRaw) : _connectionStringRaw;

        await using var con = new SqlConnection(cs);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(_sql, con) { CommandTimeout = 0 };
        foreach (var (clave, valor) in ctx.Parameters)
            cmd.Parameters.AddWithValue("@" + clave.TrimStart('@'), (object?)valor ?? DBNull.Value);

        // Carga incremental: si el SQL usa @watermark pero no vino en los parametros (primera corrida /
        // reset), declararlo como NULL para que SQL Server no exija la variable. El usuario filtra con
        // "@watermark IS NULL OR col > @watermark", asi que NULL trae todo.
        if (!cmd.Parameters.Contains("@watermark")
            && _sql.Contains("@watermark", StringComparison.OrdinalIgnoreCase))
        {
            cmd.Parameters.AddWithValue("@watermark", DBNull.Value);
        }

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();

        await foreach (var lote in RecordBatcher.Batch(cols, LeerFilas(rd, cols, ct), ctx.BatchSize, ct))
            yield return lote;
    }

    private static async IAsyncEnumerable<Record> LeerFilas(
        SqlDataReader rd, string[] cols, [EnumeratorCancellation] CancellationToken ct)
    {
        while (await rd.ReadAsync(ct))
        {
            var vals = new Dictionary<string, object?>(cols.Length);
            for (int i = 0; i < cols.Length; i++)
                vals[cols[i]] = await rd.IsDBNullAsync(i, ct) ? null : rd.GetValue(i);
            yield return new Record { Values = vals };
        }
    }
}
