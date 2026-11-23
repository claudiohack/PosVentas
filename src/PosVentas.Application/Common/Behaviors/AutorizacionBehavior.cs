using System.Reflection;
using MediatR;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;

namespace PosVentas.Application.Common.Behaviors;

/// <summary>
/// Verifica los <see cref="RequierePermisoAttribute"/> del request contra los permisos vigentes del usuario
/// (leídos de la base) y la marca <see cref="IRequiereAdministradorPlataforma"/>. Complementa la política del
/// endpoint: si un endpoint nuevo olvida su atributo, el caso de uso igual queda protegido.
/// </summary>
public sealed class AutorizacionBehavior<TRequest, TResponse>(ICurrentUserService usuarioActual)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Reflexión una sola vez por tipo de request
    private static readonly string[] PermisosRequeridos =
        typeof(TRequest).GetCustomAttributes<RequierePermisoAttribute>(inherit: false).Select(a => a.Permiso).Distinct().ToArray();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var faltante = PermisosRequeridos.FirstOrDefault(p => !usuarioActual.TienePermiso(p));
        if (faltante is not null)
            throw new AccesoDenegadoException($"Tu rol no tiene el permiso '{faltante}'.");
        if (request is IRequiereAdministradorPlataforma && !usuarioActual.EsAdministradorPlataforma)
            throw new AccesoDenegadoException("La operación es exclusiva de los administradores de la plataforma.");

        return next();
    }
}
