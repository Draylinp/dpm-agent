using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Contacto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NumeroConfirmadoUtc",
                table: "Seguimientos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Pacientes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DesactivadoUtc",
                table: "Pacientes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NoLocalizableUtc",
                table: "Pacientes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroConfirmadoUtc",
                table: "Seguimientos");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "DesactivadoUtc",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "NoLocalizableUtc",
                table: "Pacientes");
        }
    }
}
