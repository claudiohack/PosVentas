using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDebitoAutomaticoMercadoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DebitoAutomatico",
                table: "Suscripciones",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreaprobacionMercadoPagoId",
                table: "Suscripciones",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suscripciones_PreaprobacionMercadoPagoId",
                table: "Suscripciones",
                column: "PreaprobacionMercadoPagoId",
                unique: true,
                filter: "[PreaprobacionMercadoPagoId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suscripciones_PreaprobacionMercadoPagoId",
                table: "Suscripciones");

            migrationBuilder.DropColumn(
                name: "DebitoAutomatico",
                table: "Suscripciones");

            migrationBuilder.DropColumn(
                name: "PreaprobacionMercadoPagoId",
                table: "Suscripciones");
        }
    }
}
