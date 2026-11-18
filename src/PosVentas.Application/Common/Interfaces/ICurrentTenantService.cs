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
/// Usuario autenticado del request actual (claims <c>uid</c> y <c>role</c>).
/// </summary>
public interface ICurrentUserService
{
    Guid? UsuarioId { get; }
    string? Rol { get; }
}
