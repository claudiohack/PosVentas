using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Auth;

/// <summary>
/// Login: el usuario se identifica con el identificador de su comercio + usuario + contraseña.
/// Los nombres de usuario son únicos por comercio, no globales.
/// </summary>
public sealed record LoginCommand(string Comercio, string Username, string Password) : IRequest<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Comercio).NotEmpty().MaximumLength(Domain.Entities.Comercio.LongitudMaximaSlug);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(Usuario.LongitudMaximaUsername);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(AuthValidationRules.PasswordLongitudMaxima);
    }
}

public sealed class LoginCommandHandler(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator) : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var slug = request.Comercio.Trim().ToLowerInvariant();
        var comercio = await db.Comercios
            .SingleOrDefaultAsync(c => c.Slug == slug && c.EstaActivo, cancellationToken);

        if (comercio is null)
        {
            passwordHasher.Verificar(request.Password, null); // mismo costo de tiempo que un login real
            throw new CredencialesInvalidasException();
        }

        // Desde acá las consultas quedan filtradas por el comercio indicado.
        tenant.EstablecerTenant(comercio.Id);

        var username = request.Username.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios
            .SingleOrDefaultAsync(u => u.Username == username && u.EstaActivo, cancellationToken);

        if (!passwordHasher.Verificar(request.Password, usuario?.PasswordHash) || usuario is null)
            throw new CredencialesInvalidasException();

        usuario.RegistrarLogin();
        await db.SaveChangesAsync(cancellationToken);

        var token = tokenGenerator.Generar(usuario);
        return new AuthResponse(token.AccessToken, token.ExpiraUtc, usuario.Id, usuario.NombreCompleto,
            usuario.Rol.ToString(), comercio.Id, comercio.Nombre);
    }
}
