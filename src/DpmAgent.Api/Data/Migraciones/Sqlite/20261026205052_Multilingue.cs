using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Multilingue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Idioma",
                table: "Pacientes",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Idioma",
                table: "MensajesSalientes",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Idioma",
                table: "Escalamientos",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MensajeOriginal",
                table: "Escalamientos",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoEn",
                table: "Escalamientos",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Idioma",
                table: "Conversaciones",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NivelIdioma",
                table: "Conversaciones",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdiomaPredeterminado",
                table: "Consultorios",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "es");

            migrationBuilder.AddColumn<string>(
                name: "TelefonoEmergencias",
                table: "Consultorios",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "911");

            migrationBuilder.AddColumn<string>(
                name: "Idioma",
                table: "Bitacora",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumenEn",
                table: "Bitacora",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumenEs",
                table: "Bitacora",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextoOriginal",
                table: "Bitacora",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Idioma",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "Idioma",
                table: "MensajesSalientes");

            migrationBuilder.DropColumn(
                name: "Idioma",
                table: "Escalamientos");

            migrationBuilder.DropColumn(
                name: "MensajeOriginal",
                table: "Escalamientos");

            migrationBuilder.DropColumn(
                name: "MotivoEn",
                table: "Escalamientos");

            migrationBuilder.DropColumn(
                name: "Idioma",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "NivelIdioma",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "IdiomaPredeterminado",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "TelefonoEmergencias",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "Idioma",
                table: "Bitacora");

            migrationBuilder.DropColumn(
                name: "ResumenEn",
                table: "Bitacora");

            migrationBuilder.DropColumn(
                name: "ResumenEs",
                table: "Bitacora");

            migrationBuilder.DropColumn(
                name: "TextoOriginal",
                table: "Bitacora");
        }
    }
}
