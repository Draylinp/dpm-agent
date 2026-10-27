using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class CanalMedico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CodigoVenceUtc",
                table: "Medicos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoVerificacionHash",
                table: "Medicos",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefono",
                table: "Medicos",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelefonoVerificadoUtc",
                table: "Medicos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "UltimoResumen",
                table: "Medicos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropuestaMedico",
                table: "Conversaciones",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropuestaMedicoTurno",
                table: "Conversaciones",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HoraResumenMedico",
                table: "Consultorios",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "PacienteId",
                table: "Consentimientos",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "MedicoId",
                table: "Consentimientos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SolicitudesSobrecupo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    MedicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SesionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HoraAprobadaUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecididaPor = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    DecididaUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Comunicada = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadaUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesSobrecupo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesSobrecupo_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SolicitudesSobrecupo_Medicos_MedicoId",
                        column: x => x.MedicoId,
                        principalTable: "Medicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudesSobrecupo_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Medicos_Telefono",
                table: "Medicos",
                column: "Telefono");

            migrationBuilder.CreateIndex(
                name: "IX_Consentimientos_MedicoId",
                table: "Consentimientos",
                column: "MedicoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesSobrecupo_ConsultorioId_Estado",
                table: "SolicitudesSobrecupo",
                columns: new[] { "ConsultorioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesSobrecupo_MedicoId",
                table: "SolicitudesSobrecupo",
                column: "MedicoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesSobrecupo_PacienteId",
                table: "SolicitudesSobrecupo",
                column: "PacienteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Consentimientos_Medicos_MedicoId",
                table: "Consentimientos",
                column: "MedicoId",
                principalTable: "Medicos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Consentimientos_Medicos_MedicoId",
                table: "Consentimientos");

            migrationBuilder.DropTable(
                name: "SolicitudesSobrecupo");

            migrationBuilder.DropIndex(
                name: "IX_Medicos_Telefono",
                table: "Medicos");

            migrationBuilder.DropIndex(
                name: "IX_Consentimientos_MedicoId",
                table: "Consentimientos");

            migrationBuilder.DropColumn(
                name: "CodigoVenceUtc",
                table: "Medicos");

            migrationBuilder.DropColumn(
                name: "CodigoVerificacionHash",
                table: "Medicos");

            migrationBuilder.DropColumn(
                name: "Telefono",
                table: "Medicos");

            migrationBuilder.DropColumn(
                name: "TelefonoVerificadoUtc",
                table: "Medicos");

            migrationBuilder.DropColumn(
                name: "UltimoResumen",
                table: "Medicos");

            migrationBuilder.DropColumn(
                name: "PropuestaMedico",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "PropuestaMedicoTurno",
                table: "Conversaciones");

            migrationBuilder.DropColumn(
                name: "HoraResumenMedico",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "MedicoId",
                table: "Consentimientos");

            migrationBuilder.AlterColumn<int>(
                name: "PacienteId",
                table: "Consentimientos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
