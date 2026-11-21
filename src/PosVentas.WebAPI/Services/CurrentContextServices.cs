using System.Security.Claims;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;

namespace PosVentas.WebAPI.Services;

/// <summary>
/// Resuelve el comercio del request a partir del claim <c>tid</c> del JWT validado.
/// En flujos anónimos (login / alta de comercio) el handler lo fija con <see cref="EstablecerTenant"/>.
/// </summary>
public sealed class CurrentTenantService(IHttpContextAccessor httpContextAccessor) : ICurrentTenantService
{
    private Guid? _tenantEstablecido;

    public Guid? TenantId => TenantDelToken ?? _tenantEstablecido;

    public void EstablecerTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("El TenantId no puede ser vacío.", nameof(tenantId));

        var delToken = TenantDelToken;
        if (delToken.HasValue && delToken.Value != tenantId)
            throw new TenantAccessException("El request ya pertenece a otro comercio.");

        _tenantEstablecido = tenantId;
    }

    private Guid? TenantDelToken
    {
        get
        {
            var usuario = httpContextAccessor.HttpContext?.User;
            if (usuario?.Identity?.IsAuthenticated != true)
                return null;

            var valor = usuario.FindFirstValue(PosVentasClaims.TenantId);
            return Guid.TryParse(valor, out var id) && id != Guid.Empty ? id : null;
        }
    }
}

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UsuarioId =>
        Guid.TryParse(Usuario?.FindFirstValue(PosVentasClaims.UsuarioId), out var id) ? id : null;

    public string? Rol => Usuario?.FindFirstValue(PosVentasClaims.Rol);

    public bool EsAdministradorPlataforma =>
        Usuario?.FindFirstValue(PosVentasClaims.AdministradorPlataforma) == "1" && Rol == "Administrador";

    private ClaimsPrincipal? Usuario =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
