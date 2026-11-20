using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarFacturacionElectronica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstadoFacturacion",
                table: "Ventas",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NoRequerida"); // ventas previas: tickets internos

            // El CUIT del comercio pasa a guardarse normalizado (11 dígitos, sin guiones)
            migrationBuilder.Sql("UPDATE [Comercios] SET [Cuit] = REPLACE([Cuit], '-', '') WHERE [Cuit] LIKE '%-%';");

            migrationBuilder.AddColumn<bool>(
                name: "AfipProduccion",
                table: "Comercios",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CertificadoAfipProtegido",
                table: "Comercios",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificadoAfipSujeto",
                table: "Comercios",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificadoAfipVenceUtc",
                table: "Comercios",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CondicionIva",
                table: "Comercios",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DomicilioFiscal",
                table: "Comercios",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngresosBrutos",
                table: "Comercios",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InicioActividades",
                table: "Comercios",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuntoVentaAfip",
                table: "Comercios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketAccesoAfipProtegido",
                table: "Comercios",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TicketAccesoAfipVenceUtc",
                table: "Comercios",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComprobantesFiscales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VentaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TipoComprobante = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PuntoVenta = table.Column<int>(type: "int", nullable: false),
                    NumeroComprobante = table.Column<long>(type: "bigint", nullable: true),
                    Cae = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    FechaVencimientoCae = table.Column<DateOnly>(type: "date", nullable: true),
                    Resultado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ObservacionesAfip = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CuitEmisor = table.Column<string>(type: "nchar(11)", fixedLength: true, maxLength: 11, nullable: false),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    ImporteTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DocTipoReceptor = table.Column<int>(type: "int", nullable: false),
                    DocNroReceptor = table.Column<long>(type: "bigint", nullable: false),
                    Produccion = table.Column<bool>(type: "bit", nullable: false),
                    FechaSolicitudUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesFiscales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprobantesFiscales_Comercios_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComprobantesFiscales_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesFiscales_VentaId",
                table: "ComprobantesFiscales",
                column: "VentaId");

            migrationBuilder.CreateIndex(
                name: "UX_ComprobantesFiscales_Numeracion",
                table: "ComprobantesFiscales",
                columns: new[] { "TenantId", "TipoComprobante", "PuntoVenta", "NumeroComprobante" },
                unique: true,
                filter: "[Resultado] = 'Aprobado'");

            migrationBuilder.CreateIndex(
                name: "UX_ComprobantesFiscales_VentaId_Aprobado",
                table: "ComprobantesFiscales",
                column: "VentaId",
                unique: true,
                filter: "[Resultado] = 'Aprobado'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprobantesFiscales");

            migrationBuilder.DropColumn(
                name: "EstadoFacturacion",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "AfipProduccion",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "CertificadoAfipProtegido",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "CertificadoAfipSujeto",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "CertificadoAfipVenceUtc",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "CondicionIva",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "DomicilioFiscal",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "IngresosBrutos",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "InicioActividades",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "PuntoVentaAfip",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "TicketAccesoAfipProtegido",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "TicketAccesoAfipVenceUtc",
                table: "Comercios");
        }
    }
}
