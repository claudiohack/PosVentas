using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Auth;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Usuarios;

// Gestión de usuarios del comercio (RBAC). Todos los requests exigen rol Administrador
// (IRequiereAdministrador) y quedan acotados al comercio del JWT por el filtro global de tenant.

public sealed record UsuarioDto(
    Guid Id,
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    RolUsuario Rol,
    bool Activo,
    DateTime FechaCreacionUtc,
    DateTime? UltimoLoginUtc)
{
    internal static UsuarioDto Desde(Usuario u) =>
        new(u.Id, u.NombreCompleto, u.Username, u.Email, u.Rol, u.EstaActivo, u.FechaCreacionUtc, u.UltimoLoginUtc);
}

// ---------- Consultas ----------

public sealed record ObtenerUsuariosQuery : IRequest<IReadOnlyList<UsuarioDto>>, IRequiereAdministrador;

public sealed class ObtenerUsuariosQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerUsuariosQuery, IReadOnlyList<UsuarioDto>>
{
    public async Task<IReadOnlyList<UsuarioDto>> Handle(ObtenerUsuariosQuery request, CancellationToken ct) =>
        await db.Usuarios.AsNoTracking()
            .OrderByDescending(u => u.EstaActivo).ThenBy(u => u.NombreCompleto)
            .Select(u => new UsuarioDto(u.Id, u.NombreCompleto, u.Username, u.Email, u.Rol, u.EstaActivo,
                u.FechaCreacionUtc, u.UltimoLoginUtc))
            .ToListAsync(ct);
}

public sealed record ObtenerUsuarioPorIdQuery(Guid Id) : IRequest<UsuarioDto>, IRequiereAdministrador;

public sealed class ObtenerUsuarioPorIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerUsuarioPorIdQuery, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(ObtenerUsuarioPorIdQuery request, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("el usuario", request.Id);
        return UsuarioDto.Desde(usuario);
    }
}

// ---------- Alta ----------

public sealed record CrearUsuarioCommand(
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    string Password,
    RolUsuario Rol) : IRequest<UsuarioDto>, IRequiereAdministrador;

public sealed class CrearUsuarioCommandValidator : AbstractValidator<CrearUsuarioCommand>
{
    public CrearUsuarioCommandValidator()
    {
        RuleFor(x => x.NombreCompleto).NotEmpty().MaximumLength(Usuario.LongitudMaximaNombre);
        RuleFor(x => x.NombreUsuario).NotEmpty().Matches(AuthValidationRules.PatronUsername)
            .WithMessage("El usuario debe tener 3 a 50 caracteres: letras, números, punto, guion o guion bajo.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(Usuario.LongitudMaximaEmail)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).PasswordSegura();
        RuleFor(x => x.Rol).IsInEnum();
    }
}

public sealed class CrearUsuarioCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<CrearUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(CrearUsuarioCommand request, CancellationToken ct)
    {
        var username = Usuario.NormalizarUsername(request.NombreUsuario);
        if (await db.Usuarios.AnyAsync(u => u.Username == username, ct))
            throw new ConflictException($"Ya existe el usuario '{username}' en este comercio.");

        var usuario = Usuario.Crear(request.NombreCompleto, username, passwordHasher.Hash(request.Password),
            request.Rol, request.Email);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct); // el índice único (TenantId, Username) cubre la carrera entre dos altas

        return UsuarioDto.Desde(usuario);
    }
}

// ---------- Edición ----------

public sealed record ActualizarUsuarioCommand(Guid Id, string NombreCompleto, string? Email, RolUsuario Rol)
    : IRequest<UsuarioDto>, IRequiereAdministrador;

public sealed class ActualizarUsuarioCommandValidator : AbstractValidator<ActualizarUsuarioCommand>
{
    public ActualizarUsuarioCommandValidator()
    {
        RuleFor(x => x.NombreCompleto).NotEmpty().MaximumLength(Usuario.LongitudMaximaNombre);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(Usuario.LongitudMaximaEmail)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Rol).IsInEnum();
    }
}

public sealed class ActualizarUsuarioCommandHandler(IApplicationDbContext db)
    : IRequestHandler<ActualizarUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(ActualizarUsuarioCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);

        if (usuario.Rol == RolUsuario.Administrador && request.Rol != RolUsuario.Administrador)
            await GestionUsuarios.ExigirOtroAdministradorActivoAsync(db, usuario.Id, ct);

        usuario.ActualizarDatos(request.NombreCompleto, request.Email);
        usuario.CambiarRol(request.Rol);
        await db.SaveChangesAsync(ct);

        return UsuarioDto.Desde(usuario);
    }
}

// ---------- Contraseña ----------

/// <summary>Reseteo de contraseña por un administrador. Revoca los tokens vigentes del usuario.</summary>
public sealed record CambiarPasswordCommand(Guid Id, string NuevaPassword) : IRequest, IRequiereAdministrador;

public sealed class CambiarPasswordCommandValidator : AbstractValidator<CambiarPasswordCommand>
{
    public CambiarPasswordCommandValidator() => RuleFor(x => x.NuevaPassword).PasswordSegura();
}

public sealed class CambiarPasswordCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<CambiarPasswordCommand>
{
    public async Task Handle(CambiarPasswordCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);
        usuario.CambiarPasswordHash(passwordHasher.Hash(request.NuevaPassword));
        await db.SaveChangesAsync(ct);
    }
}

// ---------- Estado ----------

/// <summary>Activación / baja lógica. Al desactivar, los tokens vigentes del usuario dejan de servir.</summary>
public sealed record CambiarEstadoUsuarioCommand(Guid Id, bool Activo) : IRequest<UsuarioDto>, IRequiereAdministrador;

public sealed class CambiarEstadoUsuarioCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<CambiarEstadoUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(CambiarEstadoUsuarioCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);

        if (request.Activo)
        {
            usuario.Activar();
        }
        else
        {
            var solicitanteId = usuarioActual.UsuarioId
                ?? throw new AccesoDenegadoException("No hay un usuario autenticado.");
            usuario.Desactivar(solicitanteId); // regla de dominio: nadie se desactiva a sí mismo

            if (usuario.Rol == RolUsuario.Administrador)
                await GestionUsuarios.ExigirOtroAdministradorActivoAsync(db, usuario.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return UsuarioDto.Desde(usuario);
    }
}

internal static class GestionUsuarios
{
    public static async Task<Usuario> ObtenerAsync(IApplicationDbContext db, Guid id, CancellationToken ct) =>
        await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("el usuario", id);

    /// <summary>El comercio nunca puede quedar sin un administrador activo (no habría quién gestione usuarios).</summary>
    public static async Task ExigirOtroAdministradorActivoAsync(IApplicationDbContext db, Guid exceptoId, CancellationToken ct)
    {
        var hayOtro = await db.Usuarios.AnyAsync(
            u => u.Id != exceptoId && u.EstaActivo && u.Rol == RolUsuario.Administrador, ct);
        if (!hayOtro)
            throw new DomainException("El comercio debe conservar al menos un administrador activo.");
    }
}
