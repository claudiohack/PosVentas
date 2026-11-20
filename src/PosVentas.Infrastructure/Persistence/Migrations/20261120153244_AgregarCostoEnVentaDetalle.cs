using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCostoEnVentaDetalle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostoUnitario",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Ventas anteriores: no se guardó el costo al vender. Se estima con el costo actual del producto
            // (mejor aproximación disponible; las ventas nuevas congelan el costo real del momento).
            migrationBuilder.Sql("""
                UPDATE d SET d.CostoUnitario = p.PrecioCompra
                FROM [VentaDetalles] d
                INNER JOIN [Productos] p ON p.Id = d.ProductoId
                WHERE d.CostoUnitario = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostoUnitario",
                table: "VentaDetalles");
        }
    }
}
