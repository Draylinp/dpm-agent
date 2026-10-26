using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
{
    /// <inheritdoc />
    public partial class SesionSegura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelacionPropuesta",
                table: "Conversaciones",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelacionPropuestaTurno",
                table: "Conversaciones",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Turno",
                table: "Conversaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_ActualizadaUtc",
                table: "Conversaciones",
                column: "ActualizadaUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversaciones_ActualizadaUtc",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "CancelacionPropuesta",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "CancelacionPropuestaTurno",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "Turno",
                table: "Conversaciones");
        }
    }
}
