using PosVentas.Domain.Seguridad;

namespace PosVentas.Application.Features.Roles;

public sealed record PermisoDto(string Codigo, string Nombre, string Descripcion);

public sealed record GrupoPermisosDto(string Grupo, IReadOnlyList<PermisoDto> Permisos);

/// <summary>Textos del catálogo de permisos para la pantalla de roles, agrupados por módulo.</summary>
public static class CatalogoPermisos
{
    public static IReadOnlyList<GrupoPermisosDto> Grupos { get; } =
    [
        new("Ventas",
        [
            new(Permisos.VentasRegistrar, "Vender", "Registrar ventas en el mostrador y reintentar su factura."),
            new(Permisos.VentasConsultar, "Consultar ventas", "Ver el historial de ventas, comprobantes y anulaciones."),
            new(Permisos.VentasAnular, "Anular ventas", "Anular ventas (revierte stock, caja y cuenta corriente) y emitir la nota de crédito.")
        ]),
        new("Caja",
        [
            new(Permisos.CajaOperar, "Operar caja", "Ver las cajas, abrir turnos y consultar el resumen del turno."),
            new(Permisos.CajaCerrar, "Cerrar y arquear caja", "Cerrar el turno con el arqueo de efectivo.")
        ]),
        new("Clientes",
        [
            new(Permisos.ClientesConsultar, "Consultar clientes", "Ver clientes y sus cuentas corrientes."),
            new(Permisos.ClientesGestionar, "Gestionar clientes", "Dar de alta clientes y fijar su crédito."),
            new(Permisos.ClientesCobrar, "Cobrar cuentas corrientes", "Registrar recibos de cobranza.")
        ]),
        new("Catálogo",
        [
            new(Permisos.ProductosConsultar, "Consultar productos", "Ver productos, categorías, precios y stock."),
            new(Permisos.ProductosGestionar, "Gestionar productos", "Crear, editar, dar de baja productos y ajustar stock."),
            new(Permisos.ProductosStockMinimo, "Configurar stock mínimo", "Cambiar el umbral de alerta de stock bajo."),
            new(Permisos.ProductosExportar, "Exportar productos", "Descargar el catálogo en Excel."),
            new(Permisos.ProductosImportar, "Importar productos", "Crear y actualizar productos desde Excel."),
            new(Permisos.CategoriasGestionar, "Gestionar categorías", "Crear, editar, ordenar y dar de baja categorías.")
        ]),
        new("Compras",
        [
            new(Permisos.ComprasConsultar, "Consultar compras", "Ver las compras registradas."),
            new(Permisos.ComprasRegistrar, "Registrar compras", "Cargar compras: ingresan stock y actualizan costos."),
            new(Permisos.ProveedoresConsultar, "Consultar proveedores", "Ver la lista de proveedores."),
            new(Permisos.ProveedoresGestionar, "Gestionar proveedores", "Crear, editar y dar de baja proveedores.")
        ]),
        new("Reportes",
        [
            new(Permisos.ReportesVer, "Ver reportes", "Dashboard, rentabilidad, productos más vendidos y auditoría de cajas.")
        ]),
        new("Administración",
        [
            new(Permisos.UsuariosGestionar, "Gestionar usuarios", "Crear usuarios, asignarles un rol, resetear contraseñas y darlos de baja."),
            new(Permisos.RolesGestionar, "Gestionar roles", "Crear roles y definir sus permisos."),
            new(Permisos.FacturacionConfigurar, "Configurar facturación", "Datos fiscales y certificado de ARCA."),
            new(Permisos.AuditoriaConsultar, "Consultar auditoría", "Ver quién cambió usuarios, roles y permisos.")
        ])
    ];

    private static readonly Dictionary<string, int> Orden =
        Grupos.SelectMany(g => g.Permisos).Select((p, i) => (p.Codigo, i)).ToDictionary(x => x.Codigo, x => x.i);

    private static readonly Dictionary<string, string> Nombres =
        Grupos.SelectMany(g => g.Permisos).ToDictionary(p => p.Codigo, p => p.Nombre);

    /// <summary>Ordena códigos de permiso como en la pantalla (por módulo).</summary>
    public static IReadOnlyList<string> Ordenar(IEnumerable<string> codigos) =>
        codigos.OrderBy(c => Orden.GetValueOrDefault(c, int.MaxValue)).ThenBy(c => c, StringComparer.Ordinal).ToList();

    public static string NombreDe(string codigo) => Nombres.GetValueOrDefault(codigo, codigo);
}
