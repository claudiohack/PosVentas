using MediatR;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Common.Behaviors;

/// <summary>
/// Rechaza los requests marcados con <see cref="IRequiereAdministrador"/> si el rol del JWT no es Administrador.
/// Complementa la política del controller: si un endpoint nuevo olvida el [Authorize], el comando igual queda protegido.
/// </summary>
public sealed class AutorizacionBehavior<TRequest, TResponse>(ICurrentUserService usuarioActual)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IRequiereAdministrador && usuarioActual.Rol != nameof(RolUsuario.Administrador))
            throw new AccesoDenegadoException("La operación requiere el rol Administrador.");

        return next();
    }
}
