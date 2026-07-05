using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataPipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColumnaWatermark",
                table: "Pipelines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UltimoWatermark",
                table: "Pipelines",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColumnaWatermark",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "UltimoWatermark",
                table: "Pipelines");
        }
    }
}
