using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Common.Auditoria;

/// <summary>
/// Agrega un <see cref="RegistroAuditoria"/> al contexto. No guarda: el registro se confirma en el mismo
/// SaveChanges que el cambio auditado (si el cambio falla, tampoco queda el registro).
/// </summary>
public sealed class RegistradorAuditoria(IApplicationDbContext db, ICurrentUserService usuarioActual)
{
    public void Registrar(string accion, string entidad, Guid? entidadId, string? detalle) =>
        db.RegistrosAuditoria.Add(RegistroAuditoria.Crear(usuarioActual.UsuarioId, usuarioActual.Nombre, accion, entidad, entidadId, detalle));
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
}
