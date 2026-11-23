using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Auth;

/// <summary>
/// Alta (onboarding) de un comercio nuevo con su usuario administrador, con una invitación de un solo uso
/// emitida por HS Tech. Crea además los datos mínimos del tenant: cliente Consumidor Final,
/// categoría General y Caja Principal. La invitación se consume en la misma transacción.
/// </summary>
public sealed record RegistrarComercioCommand(
    string TokenInvitacion,
    string NombreComercio,
    string Slug,
    string? Cuit,
    string NombreAdministrador,
    string Username,
    string Password) : IRequest<AuthResponse>;

public sealed class RegistrarComercioCommandValidator : AbstractValidator<RegistrarComercioCommand>
{
    public RegistrarComercioCommandValidator()
    {
        RuleFor(x => x.TokenInvitacion).NotEmpty().MaximumLength(200)
            .WithMessage("Falta el token de la invitación.");
        RuleFor(x => x.NombreComercio).NotEmpty().MaximumLength(Comercio.LongitudMaximaNombre);
        RuleFor(x => x.Slug).NotEmpty()
            .Matches(AuthValidationRules.PatronSlug)
            .WithMessage("El identificador debe tener 3 a 50 caracteres: minúsculas, números y guiones.");
        RuleFor(x => x.Cuit).Matches(@"^\d{2}-?\d{8}-?\d$").When(x => !string.IsNullOrWhiteSpace(x.Cuit))
            .WithMessage("El CUIT debe tener 11 dígitos (con o sin guiones).");
        RuleFor(x => x.NombreAdministrador).NotEmpty().MaximumLength(Usuario.LongitudMaximaNombre);
        RuleFor(x => x.Username).NotEmpty().Matches(AuthValidationRules.PatronUsername)
            .WithMessage("El usuario debe tener 3 a 50 caracteres: letras, números, punto, guion o guion bajo.");
        RuleFor(x => x.Password).PasswordSegura();
    }
}

public sealed class RegistrarComercioCommandHandler(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator) : IRequestHandler<RegistrarComercioCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegistrarComercioCommand request, CancellationToken cancellationToken)
    {
        // La invitación se busca por el hash del token. Inexistente, usada, revocada o vencida: el mismo error
        // (no se revela cuál), y se valida antes que el slug para no permitir sondear slugs sin invitación.
        var hash = TokensInvitacion.Hash(request.TokenInvitacion);
        var invitacion = await db.InvitacionesComercio.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitacion is null || !invitacion.EstaVigente)
            throw new InvitacionInvalidaException();

        var slug = Comercio.NormalizarSlug(request.Slug);
        if (await db.Comercios.AnyAsync(c => c.Slug == slug, cancellationToken))
            throw new ConflictException($"El identificador de comercio '{slug}' ya está en uso.");

        var comercio = Comercio.Crear(request.NombreComercio, slug, request.Cuit);
        db.Comercios.Add(comercio);

        // A partir de acá todo lo que se cree queda asignado a este comercio.
        tenant.EstablecerTenant(comercio.Id);

        // Roles de sistema (Administrador, Supervisor, Cajero); el que da de alta el comercio es su Administrador.
        var roles = Rol.CrearRolesDeSistema();
        db.Roles.AddRange(roles);
        var rolAdministrador = roles.Single(r => r.EsAdministrador);

        var admin = Usuario.Crear(
            request.NombreAdministrador,
            request.Username,
            passwordHasher.Hash(request.Password),
            rolAdministrador);

        db.Usuarios.Add(admin);
        db.Clientes.Add(Cliente.CrearConsumidorFinalGenerico());
        db.Categorias.Add(Categoria.Crear("General", "Categoría por defecto", 0));
        db.Cajas.Add(Caja.Crear("Caja Principal"));

        admin.RegistrarLogin();
        invitacion.Consumir(comercio.Id);

        // Una sola transacción: comercio, datos iniciales y la invitación consumida. Su RowVersion hace que
        // dos altas simultáneas con el mismo token no puedan confirmarse ambas (la segunda recibe 409).
        await db.SaveChangesAsync(cancellationToken);

        var token = tokenGenerator.Generar(admin, rolAdministrador);
        return AuthResponse.Crear(token, admin, rolAdministrador, comercio, esAdministradorPlataforma: false);
    }
}
