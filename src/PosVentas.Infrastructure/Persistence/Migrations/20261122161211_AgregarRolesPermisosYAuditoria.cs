using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosVentas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRolesPermisosYAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
migrationBuilder.CreateTable(
                name: "RegistrosAuditoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NombreUsuario = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Accion = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Entidad = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    EntidadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosAuditoria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrosAuditoria_Comercios_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CodigoSistema = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    EstaActivo = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaModificacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Comercios_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolesPermisos",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    RolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolesPermisos", x => new { x.RolId, x.Codigo });
                    table.ForeignKey(
                        name: "FK_RolesPermisos_Roles_RolId",
                        column: x => x.RolId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Migración de datos: cada comercio recibe sus 3 roles de sistema y cada usuario el suyo ----
            migrationBuilder.AddColumn<Guid>(
                name: "RolId",
                table: "Usuarios",
                type: "uniqueidentifier",
                nullable: true);

            // Literales a propósito (no constantes del dominio): una migración publicada no debe cambiar si el código cambia.
            migrationBuilder.Sql("""
                INSERT INTO [Roles] ([Id], [Nombre], [Descripcion], [CodigoSistema], [EstaActivo], [FechaCreacionUtc], [TenantId])
                SELECT NEWID(), v.[Nombre], v.[Descripcion], v.[Codigo], 1, SYSUTCDATETIME(), c.[Id]
                FROM [Comercios] c
                CROSS JOIN (VALUES
                    ('ADMINISTRADOR', N'Administrador', N'Acceso total al comercio, incluida la gestión de usuarios y roles.'),
                    ('SUPERVISOR', N'Supervisor', N'Mostrador, cierre y arqueo de caja, anulaciones, compras y reportes.'),
                    ('CAJERO', N'Cajero', N'Vende, abre caja y registra cobros.')
                ) v([Codigo], [Nombre], [Descripcion]);
                """);

            migrationBuilder.Sql("""
                INSERT INTO [RolesPermisos] ([RolId], [Codigo])
                SELECT r.[Id], p.[Codigo]
                FROM [Roles] r
                JOIN (VALUES
                    ('CAJERO', 'ventas.registrar'), ('CAJERO', 'ventas.consultar'), ('CAJERO', 'caja.operar'),
                    ('CAJERO', 'clientes.consultar'), ('CAJERO', 'clientes.cobrar'), ('CAJERO', 'productos.consultar'),
                    ('SUPERVISOR', 'ventas.registrar'), ('SUPERVISOR', 'ventas.consultar'), ('SUPERVISOR', 'ventas.anular'),
                    ('SUPERVISOR', 'caja.operar'), ('SUPERVISOR', 'caja.cerrar'),
                    ('SUPERVISOR', 'clientes.consultar'), ('SUPERVISOR', 'clientes.cobrar'),
                    ('SUPERVISOR', 'productos.consultar'), ('SUPERVISOR', 'productos.stock-minimo'), ('SUPERVISOR', 'productos.exportar'),
                    ('SUPERVISOR', 'compras.consultar'), ('SUPERVISOR', 'compras.registrar'),
                    ('SUPERVISOR', 'proveedores.consultar'), ('SUPERVISOR', 'proveedores.gestionar'),
                    ('SUPERVISOR', 'reportes.ver')
                ) p([Rol], [Codigo]) ON p.[Rol] = r.[CodigoSistema];
                """);

            migrationBuilder.Sql("""
                UPDATE u SET u.[RolId] = r.[Id]
                FROM [Usuarios] u
                JOIN [Roles] r ON r.[TenantId] = u.[TenantId]
                    AND r.[CodigoSistema] = CASE u.[Rol] WHEN 'Administrador' THEN 'ADMINISTRADOR' WHEN 'Supervisor' THEN 'SUPERVISOR' ELSE 'CAJERO' END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RolId",
                table: "Usuarios",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Rol",
                table: "Usuarios");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_RolId",
                table: "Usuarios",
                column: "RolId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_TenantId_Entidad_EntidadId",
                table: "RegistrosAuditoria",
                columns: new[] { "TenantId", "Entidad", "EntidadId" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_TenantId_FechaCreacionUtc",
                table: "RegistrosAuditoria",
                columns: new[] { "TenantId", "FechaCreacionUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Tenant_CodigoSistema",
                table: "Roles",
                columns: new[] { "TenantId", "CodigoSistema" },
                unique: true,
                filter: "[CodigoSistema] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Tenant_Nombre_Activo",
                table: "Roles",
                columns: new[] { "TenantId", "Nombre" },
                unique: true,
                filter: "[EstaActivo] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Roles_RolId",
                table: "Usuarios",
                column: "RolId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rol",
                table: "Usuarios",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // Los roles personalizados vuelven a Cajero (el modelo anterior solo tenía tres roles fijos).
            migrationBuilder.Sql("""
                UPDATE u SET u.[Rol] = CASE r.[CodigoSistema] WHEN 'ADMINISTRADOR' THEN 'Administrador' WHEN 'SUPERVISOR' THEN 'Supervisor' ELSE 'Cajero' END
                FROM [Usuarios] u JOIN [Roles] r ON r.[Id] = u.[RolId];
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Rol",
                table: "Usuarios",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cajero",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Roles_RolId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_RolId",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RolId",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "RegistrosAuditoria");

            migrationBuilder.DropTable(
                name: "RolesPermisos");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
