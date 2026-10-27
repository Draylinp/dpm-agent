using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Seguimientos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasContactoSeguimiento",
                table: "Consultorios",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HorizonteSeguimientoSemanas",
                table: "Consultorios",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ParaReprogramar",
                table: "Citas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordatorioSieteDiasUtc",
                table: "Citas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeguimientoId",
                table: "Citas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BloqueosAgenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MedicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Desde = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Hasta = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CreadoPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloqueosAgenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BloqueosAgenda_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BloqueosAgenda_Medicos_MedicoId",
                        column: x => x.MedicoId,
                        principalTable: "Medicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Feriados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feriados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feriados_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seguimientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    MedicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    FechaObjetivo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToleranciaDias = table.Column<int>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    RecurrenciaCada = table.Column<int>(type: "INTEGER", nullable: true),
                    RecurrenciaUnidad = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    UltimoIntentoUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PorLlamada = table.Column<bool>(type: "INTEGER", nullable: false),
                    CitaId = table.Column<int>(type: "INTEGER", nullable: true),
                    SiguienteCreado = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seguimientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seguimientos_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Seguimientos_Medicos_MedicoId",
                        column: x => x.MedicoId,
                        principalTable: "Medicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Seguimientos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloqueosAgenda_ConsultorioId_MedicoId_Desde",
                table: "BloqueosAgenda",
                columns: new[] { "ConsultorioId", "MedicoId", "Desde" });

            migrationBuilder.CreateIndex(
                name: "IX_BloqueosAgenda_MedicoId",
                table: "BloqueosAgenda",
                column: "MedicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Feriados_ConsultorioId_Fecha",
                table: "Feriados",
                columns: new[] { "ConsultorioId", "Fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seguimientos_ConsultorioId_Estado",
                table: "Seguimientos",
                columns: new[] { "ConsultorioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Seguimientos_MedicoId",
                table: "Seguimientos",
                column: "MedicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Seguimientos_PacienteId",
                table: "Seguimientos",
                column: "PacienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BloqueosAgenda");

            migrationBuilder.DropTable(
                name: "Feriados");

            migrationBuilder.DropTable(
                name: "Seguimientos");

            migrationBuilder.DropColumn(
                name: "DiasContactoSeguimiento",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "HorizonteSeguimientoSemanas",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "ParaReprogramar",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "RecordatorioSieteDiasUtc",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "SeguimientoId",
                table: "Citas");
        }
    }
}
