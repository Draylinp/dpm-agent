using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class SesionesPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClaveFija",
                table: "UsuariosPanel",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DebeCambiarClave",
                table: "UsuariosPanel",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SelloSeguridad",
                table: "UsuariosPanel",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TotpPendiente",
                table: "UsuariosPanel",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecreto",
                table: "UsuariosPanel",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotpUltimoPaso",
                table: "UsuariosPanel",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TotpObligatorioAdmin",
                table: "Consultorios",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccesosDatos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Usuario = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Accion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PacienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccesosDatos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccesosDatos_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccesosDatos_ConsultorioId_FechaUtc",
                table: "AccesosDatos",
                columns: new[] { "ConsultorioId", "FechaUtc" });

            // Usuarios que ya existían: quien nunca entró tiene todavía la clave inicial y la cambia al entrar; el usuario
            // "demo" del consultorio de demostración tiene la clave publicada en la página y no se le pide cambiarla.
            migrationBuilder.Sql("UPDATE \"UsuariosPanel\" SET \"DebeCambiarClave\" = 1 WHERE \"UltimoAccesoUtc\" IS NULL AND \"Usuario\" <> 'demo'");
            migrationBuilder.Sql("UPDATE \"UsuariosPanel\" SET \"ClaveFija\" = 1 WHERE \"Usuario\" = 'demo' AND \"ConsultorioId\" IN (SELECT \"Id\" FROM \"Consultorios\" WHERE \"EsDemo\" = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccesosDatos");

            migrationBuilder.DropColumn(
                name: "ClaveFija",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "DebeCambiarClave",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "SelloSeguridad",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "TotpPendiente",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "TotpSecreto",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "TotpUltimoPaso",
                table: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "TotpObligatorioAdmin",
                table: "Consultorios");
        }
    }
}
