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

/// <summary>
/// Rol y permisos efectivos del usuario del request, leídos de la base por <c>ValidacionSesionMiddleware</c>
/// antes de la autorización. Nunca se completan desde el token. Los permisos ya vienen recortados por el plan
/// del comercio: ni el Administrador tiene los de un módulo que el plan no incluye.
/// </summary>
public sealed class SesionActual
{
    private static readonly IReadOnlySet<string> Ninguno = new HashSet<string>();

    public bool Cargada { get; private set; }
    public Guid? RolId { get; private set; }
    public bool EsAdministrador { get; private set; }
    public IReadOnlySet<string> Permisos { get; private set; } = Ninguno;

    public void Cargar(Guid rolId, bool esAdministrador, IEnumerable<string> permisosEfectivos)
    {
        if (Cargada)
            throw new InvalidOperationException("La sesión del request ya fue cargada.");
        RolId = rolId;
        EsAdministrador = esAdministrador;
        Permisos = new HashSet<string>(permisosEfectivos, StringComparer.Ordinal);
        Cargada = true;
    }
}

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, SesionActual sesion) : ICurrentUserService
{
    public Guid? UsuarioId =>
        Guid.TryParse(Usuario?.FindFirstValue(PosVentasClaims.UsuarioId), out var id) ? id : null;

    public string? Nombre => Usuario?.FindFirstValue(PosVentasClaims.Nombre);

    public Guid? RolId => sesion.Cargada ? sesion.RolId : null;

    public bool EsAdministrador => sesion.Cargada && sesion.EsAdministrador;

    public IReadOnlySet<string> Permisos => sesion.Permisos;

    public bool TienePermiso(string permiso) => sesion.Cargada && sesion.Permisos.Contains(permiso);

    public bool EsAdministradorPlataforma =>
        EsAdministrador && Usuario?.FindFirstValue(PosVentasClaims.AdministradorPlataforma) == "1";

    private ClaimsPrincipal? Usuario =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
