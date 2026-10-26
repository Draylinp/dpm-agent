using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class AgendaAutomatica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AsistenciaConfirmadaUtc",
                table: "Citas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HuecoProcesadoUtc",
                table: "Citas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordatorioEnviadoUtc",
                table: "Citas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Accion = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Entidad = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    EntidadId = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SesionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Detalle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bitacora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bitacora_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MensajesSalientes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    SesionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Canal = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Destino = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreadoUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnviadoUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesSalientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesSalientes_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfertasHueco",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    ListaEsperaId = table.Column<int>(type: "INTEGER", nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    MedicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    FechaHoraUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreadaUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RespondidaUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CitaId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfertasHueco", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfertasHueco_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfertasHueco_ListaEspera_ListaEsperaId",
                        column: x => x.ListaEsperaId,
                        principalTable: "ListaEspera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfertasHueco_Medicos_MedicoId",
                        column: x => x.MedicoId,
                        principalTable: "Medicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfertasHueco_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_ConsultorioId_FechaUtc",
                table: "Bitacora",
                columns: new[] { "ConsultorioId", "FechaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesSalientes_ConsultorioId_SesionId",
                table: "MensajesSalientes",
                columns: new[] { "ConsultorioId", "SesionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesSalientes_Estado",
                table: "MensajesSalientes",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_OfertasHueco_ConsultorioId_Estado",
                table: "OfertasHueco",
                columns: new[] { "ConsultorioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_OfertasHueco_ListaEsperaId",
                table: "OfertasHueco",
                column: "ListaEsperaId");

            migrationBuilder.CreateIndex(
                name: "IX_OfertasHueco_MedicoId_FechaHoraUtc",
                table: "OfertasHueco",
                columns: new[] { "MedicoId", "FechaHoraUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OfertasHueco_PacienteId",
                table: "OfertasHueco",
                column: "PacienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bitacora");

            migrationBuilder.DropTable(
                name: "MensajesSalientes");

            migrationBuilder.DropTable(
                name: "OfertasHueco");

            migrationBuilder.DropColumn(
                name: "AsistenciaConfirmadaUtc",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "HuecoProcesadoUtc",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "RecordatorioEnviadoUtc",
                table: "Citas");
        }
    }
}
