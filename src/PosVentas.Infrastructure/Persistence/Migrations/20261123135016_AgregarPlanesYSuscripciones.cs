using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPlanesYSuscripciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Planes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codigo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PrecioMensual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxUsuarios = table.Column<int>(type: "int", nullable: true),
                    MaxProductos = table.Column<int>(type: "int", nullable: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    EstaActivo = table.Column<bool>(type: "bit", nullable: false),
                    EsPredeterminado = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Planes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanesModulos",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanesModulos", x => new { x.PlanId, x.Codigo });
                    table.ForeignKey(
                        name: "FK_PlanesModulos_Planes_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Planes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Suscripciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnPrueba = table.Column<bool>(type: "bit", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suscripciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suscripciones_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Suscripciones_Planes_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Planes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PagosSuscripcion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuscripcionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Meses = table.Column<int>(type: "int", nullable: false),
                    PeriodoHastaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    RegistradoPorUsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosSuscripcion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosSuscripcion_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosSuscripcion_Planes_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Planes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosSuscripcion_Suscripciones_SuscripcionId",
                        column: x => x.SuscripcionId,
                        principalTable: "Suscripciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosSuscripcion_Usuarios_RegistradoPorUsuarioId",
                        column: x => x.RegistradoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagosSuscripcion_ComercioId_FechaCreacionUtc",
                table: "PagosSuscripcion",
                columns: new[] { "ComercioId", "FechaCreacionUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PagosSuscripcion_Origen_Referencia",
                table: "PagosSuscripcion",
                columns: new[] { "Origen", "Referencia" },
                unique: true,
                filter: "[Referencia] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PagosSuscripcion_PlanId",
                table: "PagosSuscripcion",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosSuscripcion_RegistradoPorUsuarioId",
                table: "PagosSuscripcion",
                column: "RegistradoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosSuscripcion_SuscripcionId",
                table: "PagosSuscripcion",
                column: "SuscripcionId");

            migrationBuilder.CreateIndex(
                name: "IX_Planes_Codigo",
                table: "Planes",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suscripciones_ComercioId",
                table: "Suscripciones",
                column: "ComercioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suscripciones_PlanId",
                table: "Suscripciones",
                column: "PlanId");

            // Planes de ejemplo: precios y límites orientativos que HS Tech edita desde la administración de planes.
            // El predeterminado (prueba gratuita) es el Full, así el comercio prueba todo durante 30 días.
            migrationBuilder.Sql("""
                INSERT INTO Planes (Id, Codigo, Nombre, Descripcion, PrecioMensual, MaxUsuarios, MaxProductos, Orden, EstaActivo, EsPredeterminado, FechaCreacionUtc)
                VALUES
                ('6f1c2a10-0000-4000-8000-000000000001', 'INICIAL', 'Inicial',
                 'Para empezar: mostrador, caja, clientes y cuenta corriente.', 15000, 2, 500, 1, 1, 0, SYSUTCDATETIME()),
                ('6f1c2a10-0000-4000-8000-000000000002', 'COMERCIO', 'Comercio',
                 'Suma facturación electrónica, compras, reportes y planillas Excel.', 30000, 5, 5000, 2, 1, 0, SYSUTCDATETIME()),
                ('6f1c2a10-0000-4000-8000-000000000003', 'FULL', 'Full',
                 'Todo incluido, sin límites de usuarios ni productos, con auditoría.', 55000, NULL, NULL, 3, 1, 1, SYSUTCDATETIME());

                INSERT INTO PlanesModulos (PlanId, Codigo)
                VALUES
                ('6f1c2a10-0000-4000-8000-000000000002', 'facturacion'),
                ('6f1c2a10-0000-4000-8000-000000000002', 'compras'),
                ('6f1c2a10-0000-4000-8000-000000000002', 'reportes'),
                ('6f1c2a10-0000-4000-8000-000000000002', 'planillas'),
                ('6f1c2a10-0000-4000-8000-000000000003', 'facturacion'),
                ('6f1c2a10-0000-4000-8000-000000000003', 'compras'),
                ('6f1c2a10-0000-4000-8000-000000000003', 'reportes'),
                ('6f1c2a10-0000-4000-8000-000000000003', 'planillas'),
                ('6f1c2a10-0000-4000-8000-000000000003', 'auditoria');
                """);

            // Comercios existentes: arrancan su prueba gratuita de 30 días con el plan predeterminado.
            migrationBuilder.Sql("""
                INSERT INTO Suscripciones (Id, ComercioId, PlanId, EnPrueba, VenceUtc, FechaCreacionUtc)
                SELECT NEWID(), c.Id, '6f1c2a10-0000-4000-8000-000000000003', 1, DATEADD(day, 30, SYSUTCDATETIME()), SYSUTCDATETIME()
                FROM Comercios c
                WHERE c.EsPlataforma = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PagosSuscripcion");

            migrationBuilder.DropTable(
                name: "PlanesModulos");

            migrationBuilder.DropTable(
                name: "Suscripciones");

            migrationBuilder.DropTable(
                name: "Planes");
        }
    }
}
