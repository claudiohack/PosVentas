using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuloCajas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cajas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComputadoraAsignada = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EstaAbierta = table.Column<bool>(type: "bit", nullable: false),
                    EstaActivo = table.Column<bool>(type: "bit", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cajas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CajaSesiones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CajaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioApertura = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UsuarioCierre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaApertura = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MontoApertura = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MontoCierreSistema = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MontoCierreReal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DiferenciaArqueo = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EnCurso = table.Column<bool>(type: "bit", nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CajaSesiones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CajaSesiones_Cajas_CajaId",
                        column: x => x.CajaId,
                        principalTable: "Cajas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cajas_EstaActivo",
                table: "Cajas",
                column: "EstaActivo");

            migrationBuilder.CreateIndex(
                name: "IX_Cajas_Nombre",
                table: "Cajas",
                column: "Nombre");

            migrationBuilder.CreateIndex(
                name: "IX_CajaSesiones_CajaId",
                table: "CajaSesiones",
                column: "CajaId");

            migrationBuilder.CreateIndex(
                name: "IX_CajaSesiones_EnCurso",
                table: "CajaSesiones",
                column: "EnCurso");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CajaSesiones");

            migrationBuilder.DropTable(
                name: "Cajas");
        }
    }
}
