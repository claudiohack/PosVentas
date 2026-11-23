using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;

namespace PosVentas.WebAPI.Security;

/// <summary>
/// Protege un endpoint con uno o más permisos (cualquiera de ellos alcanza). Ejemplo:
/// <c>[AutorizarPermiso(Permisos.VentasAnular)]</c>. Varios atributos en el mismo endpoint se exigen todos.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AutorizarPermisoAttribute(params string[] permisos) : AuthorizeAttribute(Politicas.DePermiso(permisos));

/// <summary>El usuario tiene al menos uno de los permisos (leídos de la base por el middleware de sesión).</summary>
public sealed class PermisoRequirement(IReadOnlyList<string> permisos) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permisos { get; } = permisos;
}

/// <summary>El rol del usuario es el Administrador de su comercio (según la base, no el token).</summary>
public sealed class AdministradorRequirement : IAuthorizationRequirement;

public sealed class PermisoAuthorizationHandler(ICurrentUserService usuario)
    : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requisito in context.PendingRequirements.ToList())
        {
            var cumple = requisito switch
            {
                PermisoRequirement p => p.Permisos.Any(usuario.TienePermiso),
                AdministradorRequirement => usuario.EsAdministrador,
                _ => (bool?)null
            };
            if (cumple == true)
                context.Succeed(requisito);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Crea al vuelo las políticas "perm:a|b": no hace falta registrar una política por permiso.
/// El resto (fallback, AdministradorGlobal) sale de la configuración normal.
/// </summary>
public sealed class PoliticasPermisoProvider(IOptions<AuthorizationOptions> opciones) : DefaultAuthorizationPolicyProvider(opciones)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Politicas.PrefijoPermiso, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var permisos = policyName[Politicas.PrefijoPermiso.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (permisos.Length == 0 || !permisos.All(Domain.Seguridad.Permisos.Existe))
            throw new InvalidOperationException($"La política '{policyName}' referencia un permiso inexistente.");

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(PosVentasClaims.TenantId)
            .AddRequirements(new PermisoRequirement(permisos))
            .Build();
    }
}
