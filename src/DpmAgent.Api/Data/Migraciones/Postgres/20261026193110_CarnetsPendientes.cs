using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsultorioId = table.Column<int>(type: "integer", nullable: false),
                    SesionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TelefonoCanal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PacienteId = table.Column<int>(type: "integer", nullable: true),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ars = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    NumeroAfiliado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResueltoPor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResueltoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
