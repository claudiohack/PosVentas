using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Registro inmutable (solo inserción) de una acción sensible: quién, cuándo, qué y sobre qué entidad.
/// Se guarda en la misma transacción que el cambio, así no hay cambio sin rastro ni rastro sin cambio.
/// Hoy registra los eventos de seguridad (roles y usuarios); está pensado para sumar otros módulos
/// (precios, anulaciones, configuración fiscal) con nuevas acciones, sin cambiar el esquema.
/// </summary>
public sealed class RegistroAuditoria : TenantEntity
{
    public const int LongitudMaximaAccion = 60;
    public const int LongitudMaximaEntidad = 60;
    public const int LongitudMaximaUsuario = 150;
    public const int LongitudMaximaDetalle = 2000;

    /// <summary>Usuario que ejecutó la acción (null si fue el sistema).</summary>
    public Guid? UsuarioId { get; private set; }

    /// <summary>Nombre del usuario al momento de la acción: el registro no cambia si después se renombra.</summary>
    public string? NombreUsuario { get; private set; }

    /// <summary>Código estable de la acción, p. ej. "rol.permisos-modificados".</summary>
    public string Accion { get; private set; } = string.Empty;

    public string Entidad { get; private set; } = string.Empty;
    public Guid? EntidadId { get; private set; }

    /// <summary>Descripción legible del cambio (por ejemplo, qué permisos se agregaron y quitaron).</summary>
    public string? Detalle { get; private set; }

    private RegistroAuditoria(Guid id) : base(id) { }

    // Requerido por EF Core
    private RegistroAuditoria() { }

    public static RegistroAuditoria Crear(Guid? usuarioId, string? nombreUsuario, string accion, string entidad, Guid? entidadId, string? detalle)
    {
        if (string.IsNullOrWhiteSpace(accion) || accion.Length > LongitudMaximaAccion)
            throw new DomainException("La acción de auditoría es obligatoria.");
        if (string.IsNullOrWhiteSpace(entidad) || entidad.Length > LongitudMaximaEntidad)
            throw new DomainException("La entidad de auditoría es obligatoria.");

        return new RegistroAuditoria(Guid.NewGuid())
        {
            UsuarioId = usuarioId,
            NombreUsuario = Recortar(nombreUsuario, LongitudMaximaUsuario),
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            Detalle = Recortar(detalle, LongitudMaximaDetalle)
        };
    }

    private static string? Recortar(string? texto, int maximo) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
}
