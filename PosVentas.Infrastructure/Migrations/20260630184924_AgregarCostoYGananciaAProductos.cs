using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCostoYGananciaAProductos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeGanancia",
                table: "Productos",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PrecioCompraMoneda",
                table: "Productos",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioCompraMonto",
                table: "Productos",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PorcentajeGanancia",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "PrecioCompraMoneda",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "PrecioCompraMonto",
                table: "Productos");
        }
    }
}
