using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
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
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HuecoProcesadoUtc",
                table: "Citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordatorioEnviadoUtc",
                table: "Citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsultorioId = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Accion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Entidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EntidadId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SesionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsultorioId = table.Column<int>(type: "integer", nullable: false),
                    SesionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Destino = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsultorioId = table.Column<int>(type: "integer", nullable: false),
                    ListaEsperaId = table.Column<int>(type: "integer", nullable: false),
                    PacienteId = table.Column<int>(type: "integer", nullable: false),
                    MedicoId = table.Column<int>(type: "integer", nullable: false),
                    FechaHoraUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CitaId = table.Column<int>(type: "integer", nullable: true)
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
