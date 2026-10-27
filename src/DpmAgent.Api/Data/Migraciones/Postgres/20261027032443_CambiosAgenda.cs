using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
{
    /// <inheritdoc />
    public partial class CambiosAgenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AusenciaOfrecidaUtc",
                table: "Citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reprogramacion",
                table: "Citas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReprogramacionContactoUtc",
                table: "Citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReprogramadaAId",
                table: "Citas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Urgente",
                table: "Citas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AusenciaOfrecidaUtc",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "Reprogramacion",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "ReprogramacionContactoUtc",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "ReprogramadaAId",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "Urgente",
                table: "Citas");
        }
    }
}
