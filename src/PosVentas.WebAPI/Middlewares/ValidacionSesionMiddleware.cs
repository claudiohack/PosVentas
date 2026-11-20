using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;

namespace PosVentas.WebAPI.Middlewares;

/// <summary>
/// Revocación de tokens: un JWT con firma válida solo sirve si su usuario sigue existiendo en el comercio,
/// está activo, conserva el rol del token y su sello de seguridad no cambió (contraseña, rol o estado).
/// Corre después de UseAuthentication; la consulta (por PK) ya queda filtrada por el tenant del token.
/// </summary>
public sealed class ValidacionSesionMiddleware(ILogger<ValidacionSesionMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var principal = context.User;

        // Sin autenticar, o sin comercio (la política de autorización responde 403): no hay sesión que validar.
        if (principal.Identity?.IsAuthenticated == true && principal.FindFirstValue(PosVentasClaims.TenantId) is not null)
        {
            if (!await SesionVigenteAsync(context, principal))
            {
                logger.LogInformation("Token revocado o de usuario inactivo en {Metodo} {Ruta}", context.Request.Method, context.Request.Path);
                await RechazarAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static async Task<bool> SesionVigenteAsync(HttpContext context, ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(PosVentasClaims.UsuarioId), out var usuarioId)
            || !Guid.TryParse(principal.FindFirstValue(PosVentasClaims.SelloSeguridad), out var sello))
            return false;

        var db = context.RequestServices.GetRequiredService<IApplicationDbContext>();
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.EstaActivo, u.SecurityStamp, u.Rol })
            .SingleOrDefaultAsync(context.RequestAborted);

        return usuario is not null
            && usuario.EstaActivo
            && usuario.SecurityStamp == sello
            && usuario.Rol.ToString() == principal.FindFirstValue(PosVentasClaims.Rol);
    }

    private static async Task RechazarAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"La sesion fue revocada\"";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Sesión no válida.",
            Detail = "Tu sesión expiró o fue revocada. Volvé a iniciar sesión.",
            Type = "https://httpstatuses.io/401",
            Instance = context.Request.Path
        }, options: null, contentType: "application/problem+json");
    }
}
