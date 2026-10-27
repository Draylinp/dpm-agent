using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
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

            migrationBuilder.AlterColumn<string>(
                name: "Telefono",
                table: "Pacientes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "NumeroAfiliado",
                table: "Pacientes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelefonoHash",
                table: "Pacientes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SecretosApp",
                columns: table => new
                {
                    Nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ValorProtegido = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
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

            migrationBuilder.AlterColumn<string>(
                name: "Telefono",
                table: "Pacientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "NumeroAfiliado",
                table: "Pacientes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ConsultorioId_Telefono",
                table: "Pacientes",
                columns: new[] { "ConsultorioId", "Telefono" });
        }
    }
}
