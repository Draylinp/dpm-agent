using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Urgencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CentrosEmergencia",
                table: "Consultorios",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HoraLiberarCuposUrgencia",
                table: "Consultorios",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CuposUrgencia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MedicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DiaSemana = table.Column<int>(type: "INTEGER", nullable: false),
                    Hora = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuposUrgencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CuposUrgencia_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CuposUrgencia_Medicos_MedicoId",
                        column: x => x.MedicoId,
                        principalTable: "Medicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CuposUrgencia_ConsultorioId_MedicoId_DiaSemana_Hora",
                table: "CuposUrgencia",
                columns: new[] { "ConsultorioId", "MedicoId", "DiaSemana", "Hora" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuposUrgencia_MedicoId",
                table: "CuposUrgencia",
                column: "MedicoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CuposUrgencia");

            migrationBuilder.DropColumn(
                name: "CentrosEmergencia",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "HoraLiberarCuposUrgencia",
                table: "Consultorios");
        }
    }
}
