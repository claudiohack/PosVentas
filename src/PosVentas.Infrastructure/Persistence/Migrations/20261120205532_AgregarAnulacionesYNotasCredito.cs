using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarAnulacionesYNotasCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ComprobantesFiscales_VentaId_Aprobado",
                table: "ComprobantesFiscales");

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Ventas",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Activa"); // ventas previas: vigentes

            migrationBuilder.AddColumn<Guid>(
                name: "ComprobanteAsociadoId",
                table: "ComprobantesFiscales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CantidadDevoluciones",
                table: "CajaSesiones",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDevoluciones",
                table: "CajaSesiones",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AnulacionesVenta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VentaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FechaAnulacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MontoTotalRevertido = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoDevueltoEnCaja = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoAcreditadoCuentaCorriente = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CajaSesionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EstadoNotaCredito = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnulacionesVenta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnulacionesVenta_CajaSesiones_CajaSesionId",
                        column: x => x.CajaSesionId,
                        principalTable: "CajaSesiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnulacionesVenta_Comercios_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnulacionesVenta_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnulacionesVenta_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ComprobantesFiscales_Asociado_Aprobado",
                table: "ComprobantesFiscales",
                column: "ComprobanteAsociadoId",
                unique: true,
                filter: "[Resultado] = 'Aprobado' AND [ComprobanteAsociadoId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ComprobantesFiscales_VentaId_Aprobado",
                table: "ComprobantesFiscales",
                column: "VentaId",
                unique: true,
                filter: "[Resultado] = 'Aprobado' AND [ComprobanteAsociadoId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesVenta_CajaSesionId",
                table: "AnulacionesVenta",
                column: "CajaSesionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesVenta_TenantId_FechaAnulacionUtc",
                table: "AnulacionesVenta",
                columns: new[] { "TenantId", "FechaAnulacionUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesVenta_UsuarioId",
                table: "AnulacionesVenta",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesVenta_VentaId",
                table: "AnulacionesVenta",
                column: "VentaId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ComprobantesFiscales_ComprobantesFiscales_ComprobanteAsociadoId",
                table: "ComprobantesFiscales",
                column: "ComprobanteAsociadoId",
                principalTable: "ComprobantesFiscales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComprobantesFiscales_ComprobantesFiscales_ComprobanteAsociadoId",
                table: "ComprobantesFiscales");

            migrationBuilder.DropTable(
                name: "AnulacionesVenta");

            migrationBuilder.DropIndex(
                name: "UX_ComprobantesFiscales_Asociado_Aprobado",
                table: "ComprobantesFiscales");

            migrationBuilder.DropIndex(
                name: "UX_ComprobantesFiscales_VentaId_Aprobado",
                table: "ComprobantesFiscales");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "ComprobanteAsociadoId",
                table: "ComprobantesFiscales");

            migrationBuilder.DropColumn(
                name: "CantidadDevoluciones",
                table: "CajaSesiones");

            migrationBuilder.DropColumn(
                name: "TotalDevoluciones",
                table: "CajaSesiones");

            migrationBuilder.CreateIndex(
                name: "UX_ComprobantesFiscales_VentaId_Aprobado",
                table: "ComprobantesFiscales",
                column: "VentaId",
                unique: true,
                filter: "[Resultado] = 'Aprobado'");
        }
    }
}
