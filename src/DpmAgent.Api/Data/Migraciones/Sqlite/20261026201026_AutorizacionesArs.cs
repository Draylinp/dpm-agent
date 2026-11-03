using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class AutorizacionesArs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutorizacionesArs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    CitaId = table.Column<int>(type: "INTEGER", nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Contenido = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadaUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreadaPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ActualizadaUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AprobadaUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AprobadaPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutorizacionesArs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutorizacionesArs_Citas_CitaId",
                        column: x => x.CitaId,
                        principalTable: "Citas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutorizacionesArs_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutorizacionesArs_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionesArs_CitaId",
                table: "AutorizacionesArs",
                column: "CitaId",
                unique: true,
                filter: "\"Estado\" <> 'descartada'");

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionesArs_ConsultorioId_Estado",
                table: "AutorizacionesArs",
                columns: new[] { "ConsultorioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionesArs_PacienteId",
                table: "AutorizacionesArs",
                column: "PacienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutorizacionesArs");
        }
    }
}
