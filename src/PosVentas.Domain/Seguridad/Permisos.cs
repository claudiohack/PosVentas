namespace PosVentas.Domain.Seguridad;

/// <summary>
/// Catálogo cerrado de permisos ("módulo.acción"). Es código, no datos: cada permiso está atado a endpoints
/// y comandos que lo verifican. Los roles guardan qué permisos tienen; el catálogo define cuáles existen.
/// </summary>
/// <remarks>
/// Agregar un permiso: declararlo acá, sumarlo a <see cref="Todos"/>, describirlo en el catálogo de Application
/// y protegerlo en la API. Los roles existentes NO lo reciben solos (seguro por defecto), salvo el Administrador,
/// que tiene todos implícitamente. Si un rol de sistema debe tenerlo, otorgarlo en la migración que lo introduce.
/// Nunca renombrar un código ya publicado: está guardado en la tabla RolesPermisos.
/// </remarks>
public static class Permisos
{
    public const int LongitudMaxima = 60;

    // Ventas
    public const string VentasRegistrar = "ventas.registrar";
    public const string VentasConsultar = "ventas.consultar";
    public const string VentasAnular = "ventas.anular";

    // Caja
    public const string CajaOperar = "caja.operar";
    public const string CajaCerrar = "caja.cerrar";

    // Clientes y cuentas corrientes
    public const string ClientesConsultar = "clientes.consultar";
    public const string ClientesGestionar = "clientes.gestionar";
    public const string ClientesCobrar = "clientes.cobrar";

    // Catálogo
    public const string ProductosConsultar = "productos.consultar";
    public const string ProductosGestionar = "productos.gestionar";
    public const string ProductosStockMinimo = "productos.stock-minimo";
    public const string ProductosExportar = "productos.exportar";
    public const string ProductosImportar = "productos.importar";
    public const string CategoriasGestionar = "categorias.gestionar";

    // Compras
    public const string ComprasConsultar = "compras.consultar";
    public const string ComprasRegistrar = "compras.registrar";
    public const string ProveedoresConsultar = "proveedores.consultar";
    public const string ProveedoresGestionar = "proveedores.gestionar";

    // Gestión
    public const string ReportesVer = "reportes.ver";

    // Administración
    public const string UsuariosGestionar = "usuarios.gestionar";
    public const string RolesGestionar = "roles.gestionar";
    public const string FacturacionConfigurar = "facturacion.configurar";
    public const string AuditoriaConsultar = "auditoria.consultar";

    public static IReadOnlyList<string> Todos { get; } =
    [
        VentasRegistrar, VentasConsultar, VentasAnular,
        CajaOperar, CajaCerrar,
        ClientesConsultar, ClientesGestionar, ClientesCobrar,
        ProductosConsultar, ProductosGestionar, ProductosStockMinimo, ProductosExportar, ProductosImportar, CategoriasGestionar,
        ComprasConsultar, ComprasRegistrar, ProveedoresConsultar, ProveedoresGestionar,
        ReportesVer,
        UsuariosGestionar, RolesGestionar, FacturacionConfigurar, AuditoriaConsultar
    ];

    private static readonly HashSet<string> Validos = new(Todos, StringComparer.Ordinal);

    public static bool Existe(string? permiso) => permiso is not null && Validos.Contains(permiso);
}

/// <summary>
/// Roles que se crean en cada comercio. Su <see cref="Entities.Rol.CodigoSistema"/> es estable: el código lo usa
/// para reconocerlos aunque cambie el nombre visible. El Administrador tiene todos los permisos, presentes y futuros.
/// </summary>
public static class RolesSistema
{
    public const string Administrador = "ADMINISTRADOR";
    public const string Supervisor = "SUPERVISOR";
    public const string Cajero = "CAJERO";

    public static IReadOnlyList<string> PermisosCajero { get; } =
    [
        Permisos.VentasRegistrar, Permisos.VentasConsultar,
        Permisos.CajaOperar,
        Permisos.ClientesConsultar, Permisos.ClientesCobrar,
        Permisos.ProductosConsultar
    ];

    public static IReadOnlyList<string> PermisosSupervisor { get; } =
    [
        .. PermisosCajero,
        Permisos.VentasAnular,
        Permisos.CajaCerrar,
        Permisos.ProductosStockMinimo, Permisos.ProductosExportar,
        Permisos.ComprasConsultar, Permisos.ComprasRegistrar,
        Permisos.ProveedoresConsultar, Permisos.ProveedoresGestionar,
        Permisos.ReportesVer
    ];
}
