using PosVentas.Domain.Seguridad;

namespace PosVentas.Domain.Suscripciones;

/// <summary>
/// Catálogo cerrado de módulos que un plan puede incluir. Lo que no es un módulo (vender, caja, clientes,
/// cuenta corriente, catálogo, usuarios y roles) viene en todos los planes.
/// Un módulo no incluido quita sus permisos a todos los usuarios del comercio, también al Administrador:
/// los permisos efectivos son los del rol menos los de los módulos que el plan no trae.
/// </summary>
/// <remarks>Nunca renombrar un código publicado: está guardado en la tabla PlanesModulos.</remarks>
public static class ModulosPlan
{
    public const int LongitudMaxima = 30;

    /// <summary>Factura electrónica con ARCA (configuración fiscal y emisión de facturas).</summary>
    public const string Facturacion = "facturacion";

    /// <summary>Compras y proveedores.</summary>
    public const string Compras = "compras";

    /// <summary>Dashboard y reportes de ventas.</summary>
    public const string Reportes = "reportes";

    /// <summary>Importación y exportación del catálogo en Excel.</summary>
    public const string Planillas = "planillas";

    /// <summary>Consulta de la auditoría (los registros se guardan siempre).</summary>
    public const string Auditoria = "auditoria";

    public static IReadOnlyList<string> Todos { get; } = [Facturacion, Compras, Reportes, Planillas, Auditoria];

    private static readonly Dictionary<string, string[]> PermisosDe = new(StringComparer.Ordinal)
    {
        [Facturacion] = [Permisos.FacturacionConfigurar],
        [Compras] = [Permisos.ComprasConsultar, Permisos.ComprasRegistrar, Permisos.ProveedoresConsultar, Permisos.ProveedoresGestionar],
        [Reportes] = [Permisos.ReportesVer],
        [Planillas] = [Permisos.ProductosExportar, Permisos.ProductosImportar],
        [Auditoria] = [Permisos.AuditoriaConsultar]
    };

    public static bool Existe(string? modulo) => modulo is not null && PermisosDe.ContainsKey(modulo);

    /// <summary>Permisos que dependen del módulo.</summary>
    public static IReadOnlyList<string> PermisosDelModulo(string modulo) => PermisosDe[modulo];

    /// <summary>Permisos que el plan no habilita (los de los módulos que no incluye).</summary>
    public static IReadOnlySet<string> PermisosExcluidos(IEnumerable<string> modulosDelPlan)
    {
        var incluidos = new HashSet<string>(modulosDelPlan, StringComparer.Ordinal);
        return PermisosDe.Where(m => !incluidos.Contains(m.Key)).SelectMany(m => m.Value).ToHashSet(StringComparer.Ordinal);
    }
}
