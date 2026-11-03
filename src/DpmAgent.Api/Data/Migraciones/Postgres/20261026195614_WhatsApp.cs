using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DpmAgent.Api.Data.Migraciones.Postgres
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
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plantilla",
                table: "MensajesSalientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumberId",
                table: "Consultorios",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MensajesEntrantes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsultorioId = table.Column<int>(type: "integer", nullable: false),
                    ProveedorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    MediaId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecibidoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcesadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
