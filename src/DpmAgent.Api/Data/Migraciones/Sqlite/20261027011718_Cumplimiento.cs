using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Cumplimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsentimientoOfrecidoTurno",
                table: "Conversaciones",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentimientoVersion",
                table: "Conversaciones",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreAsistente",
                table: "Consultorios",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Consentimientos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Telefono = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Canal = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    VersionTexto = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Idioma = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    RegistradoPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consentimientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consentimientos_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Consentimientos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consentimientos_ConsultorioId_PacienteId_Categoria",
                table: "Consentimientos",
                columns: new[] { "ConsultorioId", "PacienteId", "Categoria" });

            migrationBuilder.CreateIndex(
                name: "IX_Consentimientos_PacienteId",
                table: "Consentimientos",
                column: "PacienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consentimientos");

            migrationBuilder.DropColumn(
                name: "ConsentimientoOfrecidoTurno",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "ConsentimientoVersion",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "NombreAsistente",
                table: "Consultorios");
        }
    }
}
