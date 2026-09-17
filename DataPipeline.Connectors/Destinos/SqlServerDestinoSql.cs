// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: armado de SQL puro.
// Vive separado del ADO.NET para poder testearlo sin un SQL Server a mano (no hay uno en el entorno
// de desarrollo). Sin MERGE, por el SQL Server viejo: mismo criterio que TAREA 00026.
using System.Text;
using DataPipeline.Core.Abstractions;
using DataPipeline.Core.Records;

namespace DataPipeline.Connectors.Destinos;

public static class SqlServerDestinoSql
{
    /// <summary>
    /// Tipo SQL para el tipo CLR observado en el primer lote. La tabla es nuestra, asi que se elige
    /// ancho: BIGINT y DECIMAL(38,10) entran cualquier valor que venga del pipeline sin desbordar.
    /// Todo lo que no se reconoce va como texto.
    /// </summary>
    public static string TipoSql(Type? clr) => clr switch
    {
        null => "NVARCHAR(MAX)",
        _ when clr == typeof(byte) || clr == typeof(short) || clr == typeof(int) || clr == typeof(long) => "BIGINT",
        _ when clr == typeof(decimal) => "DECIMAL(38,10)",
        _ when clr == typeof(float) || clr == typeof(double) => "FLOAT",
        _ when clr == typeof(bool) => "BIT",
        _ when clr == typeof(DateTime) || clr == typeof(DateTimeOffset) => "DATETIME2",
        _ when clr == typeof(Guid) => "UNIQUEIDENTIFIER",
        _ => "NVARCHAR(MAX)"
    };

    /// <summary>
    /// Parte "esquema.tabla" (con o sin corchetes). Sin esquema explicito, dbo. Respeta las reglas de
    /// T-SQL: entre corchetes "]]" es un "]" literal y un punto no separa. No alcanza con sacar los
    /// corchetes con Replace: eso destruye un "]" legitimo del nombre y parte mal "[mi.esquema].[t]".
    /// </summary>
    public static (string Esquema, string Tabla) Partes(string nombre)
    {
        var partes = new List<string>();
        var actual = new StringBuilder();
        for (int i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (c == '[')
            {
                for (i++; i < nombre.Length; i++)
                {
                    if (nombre[i] != ']') { actual.Append(nombre[i]); continue; }
                    if (i + 1 < nombre.Length && nombre[i + 1] == ']') { actual.Append(']'); i++; continue; }
                    break;
                }
            }
            else if (c == '.')
            {
                partes.Add(actual.ToString());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }
        partes.Add(actual.ToString());

        // Un nombre de tres partes (base.esquema.tabla) apuntaria a otra base: se rechaza en vez de
        // escribir en silencio en una tabla que no es la configurada.
        if (partes.Count > 2 || partes.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Nombre de tabla destino invalido: '{nombre}'. Usar 'tabla' o 'esquema.tabla'.", nameof(nombre));

        return partes.Count == 1 ? ("dbo", partes[0]) : (partes[0], partes[1]);
    }

    private static string Q(string id) => "[" + id.Replace("]", "]]") + "]";

    /// <summary>
    /// CREATE TABLE idempotente. Las columnas de negocio van NULL (el pipeline puede traer nulos);
    /// _key es la PK y _creado es la marca que el sistema destino usa para saber que es nuevo.
    /// _procesado NO viaja: es el ack de nuestros consumidores PULL.
    /// </summary>
    public static string CrearTabla(string tabla, IReadOnlyList<ColumnaDestino> columnas)
    {
        var (esquema, nombre) = Partes(tabla);
        var sb = new StringBuilder();
        sb.AppendLine($"IF OBJECT_ID(N'{Q(esquema)}.{Q(nombre)}', N'U') IS NULL");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    CREATE TABLE {Q(esquema)}.{Q(nombre)} (");
        foreach (var c in columnas)
            sb.AppendLine($"        {Q(c.Nombre)} {TipoSql(c.TipoClr)} NULL,");
        sb.AppendLine($"        {Q(ServingColumns.Key)} NVARCHAR(450) NOT NULL,");
        sb.AppendLine($"        {Q(ServingColumns.Creado)} DATETIME2 NOT NULL,");
        sb.AppendLine($"        CONSTRAINT {Q("PK_" + nombre)} PRIMARY KEY ({Q(ServingColumns.Key)})");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        return sb.ToString();
    }

    /// <summary>Nombre del parametro de una columna: @p{indice}, salvo _key que es @p_key.</summary>
    public static string Parametro(string columna, int indice) =>
        columna == ServingColumns.Key ? "@p_key" : "@p" + indice;

    /// <summary>
    /// Upsert de una fila por _key. _key tiene nombre fijo (@p_key) porque se referencia en los dos
    /// WHERE; el resto de las columnas usa su indice.
    /// </summary>
    public static string Upsert(string tabla, IReadOnlyList<string> columnas)
    {
        var (esquema, nombre) = Partes(tabla);
        var destino = $"{Q(esquema)}.{Q(nombre)}";

        // Diccionario armado por indice: IReadOnlyList<T> no tiene IndexOf.
        var param = new Dictionary<string, string>(columnas.Count);
        for (int i = 0; i < columnas.Count; i++)
            param[columnas[i]] = Parametro(columnas[i], i);

        var sets = columnas
            .Where(c => c != ServingColumns.Key)
            .Select(c => $"{Q(c)} = {param[c]}");

        var cols = string.Join(", ", columnas.Select(Q));
        var vals = string.Join(", ", columnas.Select(c => param[c]));

        var sb = new StringBuilder();
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM {destino} WHERE {Q(ServingColumns.Key)} = @p_key)");
        sb.AppendLine($"    UPDATE {destino} SET {string.Join(", ", sets)} WHERE {Q(ServingColumns.Key)} = @p_key;");
        sb.AppendLine("ELSE");
        sb.AppendLine($"    INSERT INTO {destino} ({cols}) VALUES ({vals});");
        return sb.ToString();
    }
}
