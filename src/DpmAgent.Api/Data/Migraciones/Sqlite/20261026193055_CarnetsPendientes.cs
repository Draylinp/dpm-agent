using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class CarnetsPendientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarnetsPendientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    SesionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TelefonoCanal = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: true),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Ars = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    NumeroAfiliado = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResueltoPor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ResueltoUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnetsPendientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarnetsPendientes_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarnetsPendientes_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarnetsPendientes_ConsultorioId_Estado",
                table: "CarnetsPendientes",
                columns: new[] { "ConsultorioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_CarnetsPendientes_ConsultorioId_SesionId",
                table: "CarnetsPendientes",
                columns: new[] { "ConsultorioId", "SesionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CarnetsPendientes_PacienteId",
                table: "CarnetsPendientes",
                column: "PacienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarnetsPendientes");
        }
    }
}
