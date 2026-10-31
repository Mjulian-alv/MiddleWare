using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace DataPipeline.Persistence;

/// <summary>
/// Pone al dia el esquema del almacen A (metadata) de forma idempotente y segura sobre bases
/// existentes. Reemplaza EnsureCreated: corre DDL (CREATE TABLE IF NOT EXISTS + ALTER ADD COLUMN)
/// tragando "duplicate column"/"already exists". Es la fuente de verdad del esquema en runtime;
/// al cambiar el modelo EF hay que agregar aca la linea ALTER ADD COLUMN correspondiente.
/// </summary>
public static class MetadataSchemaInitializer
{
    private static readonly string[] Sentencias =
    {
        // Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Origenes de archivo (Csv/Xlsx): 6 columnas
        // nuevas en Origenes + ColumnaWatermarkTipo en Pipelines. FilePath queda en la tabla (columna
        // huerfana inofensiva) para no tocar bases existentes; ya no se usa desde el C#.
        """
        CREATE TABLE IF NOT EXISTS "Origenes" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Origenes" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Tipo"             TEXT    NOT NULL,
            "ConnectionString" TEXT    NULL,
            "Username"         TEXT    NULL,
            "Password"         TEXT    NULL,
            "BaseUrl"          TEXT    NULL,
            "FilePath"         TEXT    NULL,
            "RutaCarpeta"      TEXT    NULL,
            "PatronArchivo"    TEXT    NULL,
            "Separador"        TEXT    NULL,
            "Encoding"         TEXT    NULL,
            "TieneEncabezado"  INTEGER NOT NULL DEFAULT 1,
            "HojaExcel"        TEXT    NULL,
            "UsuarioCatalogo"  TEXT    NULL,
            "PasswordCatalogo" TEXT    NULL,
            "Activo"           INTEGER NOT NULL,
            "CreatedAt"        TEXT    NOT NULL,
            "UpdatedAt"        TEXT    NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Origenes_Nombre" ON "Origenes" ("Nombre")""",
        """
        CREATE TABLE IF NOT EXISTS "Pipelines" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Pipelines" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Habilitado"       INTEGER NOT NULL,
            "Programacion"     TEXT    NOT NULL,
            "HoraDelDia"       TEXT    NULL,
            "DiaDeLaSemana"    INTEGER NULL,
            "DiaDelMes"        INTEGER NULL,
            "ProximaCorrida"   TEXT    NULL,
            "UltimaCorrida"    TEXT    NULL,
            "Pasos"            TEXT    NOT NULL,
            "TablaDestino"     TEXT    NOT NULL,
            "ModoEscritura"    TEXT    NOT NULL,
            "CorrerDespuesDe"  TEXT    NOT NULL,
            "ColumnasClave"    TEXT    NOT NULL DEFAULT '[]',
            "EsquemaOrigen"    TEXT    NOT NULL DEFAULT '[]',
            "ColumnaWatermark" TEXT    NULL,
            "UltimoWatermark"  TEXT    NULL,
            "ColumnaWatermarkTipo" TEXT NULL,
            "FilasPorLote"     INTEGER NOT NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Pipelines_Nombre"         ON "Pipelines" ("Nombre")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Pipelines_ProximaCorrida" ON "Pipelines" ("ProximaCorrida")""",
        """
        CREATE TABLE IF NOT EXISTS "Corridas" (
            "Id"            TEXT    NOT NULL CONSTRAINT "PK_Corridas" PRIMARY KEY,
            "PipelineId"    TEXT    NOT NULL,
            "Inicio"        TEXT    NOT NULL,
            "Fin"           TEXT    NULL,
            "Estado"        TEXT    NOT NULL,
            "FilasLeidas"   INTEGER NOT NULL,
            "FilasEscritas" INTEGER NOT NULL,
            "FilasError"    INTEGER NOT NULL,
            "Mensaje"       TEXT    NULL
        )
        """,
        """CREATE INDEX IF NOT EXISTS "IX_Corridas_Inicio" ON "Corridas" ("Inicio")""",
        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: tabla Envios.
        """
        CREATE TABLE IF NOT EXISTS "Envios" (
            "Id"               TEXT    NOT NULL CONSTRAINT "PK_Envios" PRIMARY KEY,
            "Nombre"           TEXT    NOT NULL,
            "Habilitado"       INTEGER NOT NULL DEFAULT 1,
            "PipelineId"       TEXT    NOT NULL,
            "DataSourceId"     TEXT    NOT NULL,
            "TablaDestino"     TEXT    NOT NULL,
            "Pendiente"        INTEGER NOT NULL DEFAULT 0,
            "InicioEnCurso"    TEXT    NULL,
            "UltimoCreado"     TEXT    NULL,
            "IntentosFallidos" INTEGER NOT NULL DEFAULT 0,
            "ProximoIntento"   TEXT    NULL,
            "UltimoEnvio"      TEXT    NULL,
            "UltimoError"      TEXT    NULL
        )
        """,
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Envios_Nombre"     ON "Envios" ("Nombre")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Envios_PipelineId" ON "Envios" ("PipelineId")""",
        """CREATE INDEX        IF NOT EXISTS "IX_Envios_Pendiente"  ON "Envios" ("Pendiente")""",
        // Columnas agregadas despues del esquema inicial (auto-cura de bases viejas).
        // Sobre base nueva fallan con "duplicate column" (tragado). Requeridas llevan DEFAULT.
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnasClave"    TEXT NOT NULL DEFAULT '[]'""",
        """ALTER TABLE "Pipelines" ADD COLUMN "EsquemaOrigen"    TEXT NOT NULL DEFAULT '[]'""",
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnaWatermark" TEXT NULL""",
        """ALTER TABLE "Pipelines" ADD COLUMN "UltimoWatermark"  TEXT NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "RutaCarpeta"          TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "PatronArchivo"        TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "Separador"            TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "Encoding"             TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "TieneEncabezado"      INTEGER NOT NULL DEFAULT 1""",
        """ALTER TABLE "Origenes"  ADD COLUMN "HojaExcel"            TEXT    NULL""",
        """ALTER TABLE "Pipelines" ADD COLUMN "ColumnaWatermarkTipo" TEXT    NULL""",
        // Fecha: 17/09/2026 - TAREA: 00038 - Linea: 2 - Encadenamiento pull/push: EnvioId distingue
        // la corrida de envio de la de pipeline en la misma tabla de historial.
        """ALTER TABLE "Corridas"  ADD COLUMN "EnvioId"              TEXT    NULL""",
        // Fecha: 31/10/2026 - TAREA: 00152 - Linea: 2 - Origen DataHub CompLook: la credencial del
        // catalogo es distinta de la de datos (ver DataSource), asi que son dos columnas aparte.
        """ALTER TABLE "Origenes"  ADD COLUMN "UsuarioCatalogo"      TEXT    NULL""",
        """ALTER TABLE "Origenes"  ADD COLUMN "PasswordCatalogo"     TEXT    NULL""",
    };

    public static async Task EnsureAsync(DbConnection connection, ILogger? logger, CancellationToken ct = default)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        foreach (var sql in Sentencias)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex) when (
                ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // La tabla/columna/indice ya existe — caso normal sobre una base existente.
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Metadata schema: {Msg}", ex.Message);
            }
        }
    }
}
