using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class UsoModelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsosModelo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Proveedor = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Modelo = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Proposito = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SesionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TokensEntrada = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensSalida = table.Column<int>(type: "INTEGER", nullable: false),
                    Milisegundos = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsosModelo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsosModelo_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsosModelo_ConsultorioId_FechaUtc",
                table: "UsosModelo",
                columns: new[] { "ConsultorioId", "FechaUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsosModelo");
        }
    }
}
