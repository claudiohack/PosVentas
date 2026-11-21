using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarInvitacionesYCodigoBarras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoBarras",
                table: "Productos",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsPlataforma",
                table: "Comercios",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InvitacionesComercio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    EmailDestinatario = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreadaPorUsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ComercioCreadoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocadaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitacionesComercio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitacionesComercio_Comercios_ComercioCreadoId",
                        column: x => x.ComercioCreadoId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitacionesComercio_Usuarios_CreadaPorUsuarioId",
                        column: x => x.CreadaPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Productos_TenantId_CodigoBarras",
                table: "Productos",
                columns: new[] { "TenantId", "CodigoBarras" },
                unique: true,
                filter: "[CodigoBarras] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Comercios_Plataforma",
                table: "Comercios",
                column: "EsPlataforma",
                unique: true,
                filter: "[EsPlataforma] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InvitacionesComercio_ComercioCreadoId",
                table: "InvitacionesComercio",
                column: "ComercioCreadoId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitacionesComercio_CreadaPorUsuarioId",
                table: "InvitacionesComercio",
                column: "CreadaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitacionesComercio_TokenHash",
                table: "InvitacionesComercio",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvitacionesComercio");

            migrationBuilder.DropIndex(
                name: "IX_Productos_TenantId_CodigoBarras",
                table: "Productos");

            migrationBuilder.DropIndex(
                name: "UX_Comercios_Plataforma",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "CodigoBarras",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "EsPlataforma",
                table: "Comercios");
        }
    }
}
