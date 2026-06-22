using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataPipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarColumnasClave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColumnasClave",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColumnasClave",
                table: "Pipelines");
        }
    }
}
