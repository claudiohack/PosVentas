namespace PosVentas.Application.Common.Interfaces;

/// <summary>
/// Comercio (tenant) del request actual. Se obtiene del claim <c>tid</c> del JWT.
/// </summary>
public interface ICurrentTenantService
{
    /// <summary>TenantId vigente, o null si el request es anónimo y aún no se estableció.</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Fija el tenant en flujos anónimos (login y alta de comercio), antes de que exista un token.
    /// Lanza excepción si el request ya trae un tenant distinto en su token.
    /// </summary>
    void EstablecerTenant(Guid tenantId);
}

/// <summary>
/// Usuario autenticado del request actual. La identidad sale del JWT (<c>uid</c>, <c>name</c>); el rol y los
/// permisos, de la base, validados en cada request (nunca del token).
/// </summary>
public interface ICurrentUserService
{
    Guid? UsuarioId { get; }
    string? Nombre { get; }

    /// <summary>Rol vigente del usuario (null si el request es anónimo).</summary>
    Guid? RolId { get; }

    /// <summary>El rol del usuario es el Administrador del comercio (todo el catálogo, dentro de lo que incluye el plan).</summary>
    bool EsAdministrador { get; }

    /// <summary>
    /// Permisos efectivos del usuario: los del rol (el Administrador, todo el catálogo) menos los de módulos
    /// que el plan del comercio no incluye.
    /// </summary>
    IReadOnlySet<string> Permisos { get; }

    bool TienePermiso(string permiso);

    /// <summary>True si es un administrador del comercio operador de la plataforma.</summary>
    bool EsAdministradorPlataforma { get; }
}
