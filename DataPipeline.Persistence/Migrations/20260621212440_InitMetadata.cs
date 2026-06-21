using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataPipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Corridas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Inicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Fin = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    FilasLeidas = table.Column<long>(type: "INTEGER", nullable: false),
                    FilasEscritas = table.Column<long>(type: "INTEGER", nullable: false),
                    FilasError = table.Column<long>(type: "INTEGER", nullable: false),
                    Mensaje = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Corridas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    Habilitado = table.Column<bool>(type: "INTEGER", nullable: false),
                    Programacion = table.Column<string>(type: "TEXT", nullable: false),
                    HoraDelDia = table.Column<string>(type: "TEXT", nullable: true),
                    DiaDeLaSemana = table.Column<int>(type: "INTEGER", nullable: true),
                    DiaDelMes = table.Column<int>(type: "INTEGER", nullable: true),
                    ProximaCorrida = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UltimaCorrida = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Pasos = table.Column<string>(type: "TEXT", nullable: false),
                    TablaDestino = table.Column<string>(type: "TEXT", nullable: false),
                    ModoEscritura = table.Column<string>(type: "TEXT", nullable: false),
                    CorrerDespuesDe = table.Column<string>(type: "TEXT", nullable: false),
                    FilasPorLote = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pipelines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Corridas_Inicio",
                table: "Corridas",
                column: "Inicio");

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_Nombre",
                table: "Pipelines",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_ProximaCorrida",
                table: "Pipelines",
                column: "ProximaCorrida");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Corridas");

            migrationBuilder.DropTable(
                name: "Pipelines");
        }
    }
}
