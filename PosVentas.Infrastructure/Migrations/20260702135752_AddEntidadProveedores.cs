using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntidadProveedores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Proveedores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RazonSocial = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Cuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TipoResponsabilidadIva = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Responsable Inscripto"),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    NombreContacto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PermiteCuentaCorriente = table.Column<bool>(type: "bit", nullable: false),
                    DiasPlazoPago = table.Column<int>(type: "int", nullable: false),
                    EstaActivo = table.Column<bool>(type: "bit", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proveedores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proveedores_Cuit",
                table: "Proveedores",
                column: "Cuit");

            migrationBuilder.CreateIndex(
                name: "IX_Proveedores_EstaActivo",
                table: "Proveedores",
                column: "EstaActivo");

            migrationBuilder.CreateIndex(
                name: "IX_Proveedores_RazonSocial",
                table: "Proveedores",
                column: "RazonSocial");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Proveedores");
        }
    }
}
