using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

/// <summary>
/// Códigos de permiso (copia del catálogo de la API: el cliente no referencia al dominio).
/// Solo deciden qué se muestra; la API vuelve a verificar cada permiso en cada request.
/// </summary>
public static class Permisos
{
    public const string VentasRegistrar = "ventas.registrar";
    public const string VentasConsultar = "ventas.consultar";
    public const string VentasAnular = "ventas.anular";
    public const string CajaOperar = "caja.operar";
    public const string CajaCerrar = "caja.cerrar";
    public const string ClientesConsultar = "clientes.consultar";
    public const string ClientesGestionar = "clientes.gestionar";
    public const string ClientesCobrar = "clientes.cobrar";
    public const string ProductosConsultar = "productos.consultar";
    public const string ProductosGestionar = "productos.gestionar";
    public const string ProductosStockMinimo = "productos.stock-minimo";
    public const string ProductosExportar = "productos.exportar";
    public const string ProductosImportar = "productos.importar";
    public const string CategoriasGestionar = "categorias.gestionar";
    public const string ComprasConsultar = "compras.consultar";
    public const string ComprasRegistrar = "compras.registrar";
    public const string ProveedoresConsultar = "proveedores.consultar";
    public const string ProveedoresGestionar = "proveedores.gestionar";
    public const string ReportesVer = "reportes.ver";
    public const string UsuariosGestionar = "usuarios.gestionar";
    public const string RolesGestionar = "roles.gestionar";
    public const string FacturacionConfigurar = "facturacion.configurar";
    public const string AuditoriaConsultar = "auditoria.consultar";
}

public sealed record RolDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? CodigoSistema,
    bool EsSistema,
    bool EsAdministrador,
    bool Activo,
    List<string> Permisos,
    int CantidadUsuarios);

public sealed record PermisoDto(string Codigo, string Nombre, string Descripcion);

public sealed record GrupoPermisosDto(string Grupo, List<PermisoDto> Permisos);

public sealed record SesionDto(
    Guid UsuarioId,
    string NombreCompleto,
    Guid RolId,
    string Rol,
    bool EsAdministrador,
    List<string> Permisos,
    Guid ComercioId,
    string Comercio,
    bool EsAdministradorPlataforma);

public sealed class RolFormulario
{
    [Required(ErrorMessage = "Ingresá el nombre del rol.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "El nombre debe tener entre 3 y 50 caracteres.")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Descripcion { get; set; }

    public HashSet<string> Permisos { get; set; } = [];

    public static RolFormulario Desde(RolDto r) => new()
    {
        Nombre = r.Nombre,
        Descripcion = r.Descripcion,
        Permisos = [.. r.Permisos]
    };
}

public sealed record RegistroAuditoriaDto(
    Guid Id, DateTime FechaUtc, Guid? UsuarioId, string? Usuario, string Accion, string Entidad, Guid? EntidadId, string? Detalle);

public sealed record PaginaAuditoriaDto(List<RegistroAuditoriaDto> Registros, int Total, int Pagina, int TamanioPagina);
