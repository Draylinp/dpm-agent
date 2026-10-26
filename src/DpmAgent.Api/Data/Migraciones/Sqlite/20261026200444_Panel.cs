using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class Panel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AtendidoPor",
                table: "Escalamientos",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UsuariosPanel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Usuario = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    HashClave = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "INTEGER", nullable: false),
                    BloqueadoHastaUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreadoUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UltimoAccesoUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosPanel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsuariosPanel_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosPanel_ConsultorioId",
                table: "UsuariosPanel",
                column: "ConsultorioId");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosPanel_Usuario",
                table: "UsuariosPanel",
                column: "Usuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsuariosPanel");

            migrationBuilder.DropColumn(
                name: "AtendidoPor",
                table: "Escalamientos");
        }
    }
}
