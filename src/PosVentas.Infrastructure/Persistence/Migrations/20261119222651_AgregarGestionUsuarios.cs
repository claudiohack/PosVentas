using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarGestionUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Usuarios",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "Usuarios",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // usuarios existentes: sello aleatorio, no Guid vacío
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Usuarios");
        }
    }
}
