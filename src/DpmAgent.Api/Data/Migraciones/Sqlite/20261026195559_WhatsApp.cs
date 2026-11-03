using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Sqlite
{
    /// <inheritdoc />
    public partial class WhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParametrosJson",
                table: "MensajesSalientes",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plantilla",
                table: "MensajesSalientes",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumberId",
                table: "Consultorios",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MensajesEntrantes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsultorioId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProveedorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Telefono = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    MediaId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RecibidoUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcesadoUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesEntrantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesEntrantes_Consultorios_ConsultorioId",
                        column: x => x.ConsultorioId,
                        principalTable: "Consultorios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consultorios_WhatsAppPhoneNumberId",
                table: "Consultorios",
                column: "WhatsAppPhoneNumberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MensajesEntrantes_ConsultorioId",
                table: "MensajesEntrantes",
                column: "ConsultorioId");

            migrationBuilder.CreateIndex(
                name: "IX_MensajesEntrantes_Estado",
                table: "MensajesEntrantes",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MensajesEntrantes_ProveedorId",
                table: "MensajesEntrantes",
                column: "ProveedorId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MensajesEntrantes");

            migrationBuilder.DropIndex(
                name: "IX_Consultorios_WhatsAppPhoneNumberId",
                table: "Consultorios");

            migrationBuilder.DropColumn(
                name: "ParametrosJson",
                table: "MensajesSalientes");

            migrationBuilder.DropColumn(
                name: "Plantilla",
                table: "MensajesSalientes");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumberId",
                table: "Consultorios");
        }
    }
}
