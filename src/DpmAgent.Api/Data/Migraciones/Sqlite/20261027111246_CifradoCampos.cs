using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class CifradoCampos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ConsultorioId_Telefono",
                table: "Pacientes");

            migrationBuilder.AddColumn<string>(
                name: "TelefonoHash",
                table: "Pacientes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SecretosApp",
                columns: table => new
                {
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ValorProtegido = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretosApp", x => x.Nombre);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ConsultorioId_TelefonoHash",
                table: "Pacientes",
                columns: new[] { "ConsultorioId", "TelefonoHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecretosApp");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ConsultorioId_TelefonoHash",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "TelefonoHash",
                table: "Pacientes");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ConsultorioId_Telefono",
                table: "Pacientes",
                columns: new[] { "ConsultorioId", "Telefono" });
        }
    }
}
