using System.Globalization;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Common.Auditoria;

/// <summary>
/// Agrega un <see cref="RegistroAuditoria"/> al contexto. No guarda: el registro se confirma en el mismo
/// SaveChanges que el cambio auditado (si el cambio falla, tampoco queda el registro).
/// </summary>
public sealed class RegistradorAuditoria(IApplicationDbContext db, ICurrentUserService usuarioActual)
{
    private static readonly CultureInfo Argentina = CultureInfo.GetCultureInfo("es-AR");

    public void Registrar(string accion, string entidad, Guid? entidadId, string? detalle) =>
        db.RegistrosAuditoria.Add(RegistroAuditoria.Crear(usuarioActual.UsuarioId, usuarioActual.Nombre, accion, entidad, entidadId, detalle));

    /// <summary>Importe legible para el detalle ("$ 1.234,50"), independiente de la cultura del servidor.</summary>
    public static string Importe(decimal monto) => monto.ToString("C2", Argentina);

    /// <summary>Cantidad legible para el detalle, sin decimales sobrantes ("12" o "1,5").</summary>
    public static string Cantidad(decimal cantidad) => cantidad.ToString("0.###", Argentina);
}

/// <summary>Códigos estables de acciones auditadas (se consultan y filtran por estos valores).</summary>
public static class AccionesAuditoria
{
    public const string RolCreado = "rol.creado";
    public const string RolModificado = "rol.modificado";
    public const string RolEliminado = "rol.eliminado";

    public const string UsuarioCreado = "usuario.creado";
    public const string UsuarioModificado = "usuario.modificado";
    public const string UsuarioRolCambiado = "usuario.rol-cambiado";
    public const string UsuarioPasswordReseteada = "usuario.password-reseteada";
    public const string UsuarioActivado = "usuario.activado";
    public const string UsuarioDesactivado = "usuario.desactivado";

    public const string VentaAnulada = "venta.anulada";

    public const string CajaAbierta = "caja.abierta";
    public const string CajaCerrada = "caja.cerrada";

    public const string ProductoCreado = "producto.creado";
    public const string ProductoPrecioModificado = "producto.precio-modificado";
    public const string ProductoStockIngresado = "producto.stock-ingresado";
    public const string ProductoActivado = "producto.activado";
    public const string ProductoDesactivado = "producto.desactivado";
    public const string ProductosImportados = "productos.importados";

    public const string FacturacionDatosModificados = "facturacion.datos-modificados";
    public const string FacturacionCertificadoCargado = "facturacion.certificado-cargado";
}
