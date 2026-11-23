using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Auditoria;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Common.Suscripciones;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Roles;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Seguridad;

namespace PosVentas.Application.Features.Usuarios;

// Gestión de usuarios del comercio. Exige el permiso usuarios.gestionar y queda acotada al comercio del JWT
// por el filtro global de tenant. Sin escalada: quien no es Administrador no asigna ni administra usuarios
// de roles con permisos que él no tiene (así no puede, por ejemplo, resetear la clave de un Administrador).

public sealed record UsuarioDto(
    Guid Id,
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    Guid RolId,
    string Rol,
    bool Activo,
    DateTime FechaCreacionUtc,
    DateTime? UltimoLoginUtc);

internal static class GestionUsuarios
{
    public static IQueryable<UsuarioDto> Proyectar(IQueryable<Usuario> usuarios) =>
        usuarios.Select(u => new UsuarioDto(u.Id, u.NombreCompleto, u.Username, u.Email, u.RolId, u.Rol!.Nombre, u.EstaActivo,
            u.FechaCreacionUtc, u.UltimoLoginUtc));

    public static UsuarioDto ToDto(Usuario u, Rol rol) =>
        new(u.Id, u.NombreCompleto, u.Username, u.Email, rol.Id, rol.Nombre, u.EstaActivo, u.FechaCreacionUtc, u.UltimoLoginUtc);

    public static async Task<Usuario> ObtenerAsync(IApplicationDbContext db, Guid id, CancellationToken ct) =>
        await db.Usuarios.Include(u => u.Rol).ThenInclude(r => r!.Permisos).SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("el usuario", id);

    /// <summary>Rol asignable: existe en este comercio (si no, 404) y está activo.</summary>
    public static async Task<Rol> ObtenerRolAsignableAsync(IApplicationDbContext db, Guid rolId, CancellationToken ct)
    {
        var rol = await db.Roles.Include(r => r.Permisos).SingleOrDefaultAsync(r => r.Id == rolId, ct)
                  ?? throw new NotFoundException("el rol", rolId);
        if (!rol.EstaActivo)
            throw new DomainException($"El rol '{rol.Nombre}' fue eliminado y no se puede asignar.");
        return rol;
    }

    /// <summary>El comercio nunca puede quedar sin un administrador activo (no habría quién gestione usuarios).</summary>
    public static async Task ExigirOtroAdministradorActivoAsync(IApplicationDbContext db, Guid exceptoId, CancellationToken ct)
    {
        var hayOtro = await db.Usuarios.AnyAsync(
            u => u.Id != exceptoId && u.EstaActivo && u.Rol!.CodigoSistema == RolesSistema.Administrador, ct);
        if (!hayOtro)
            throw new DomainException("El comercio debe conservar al menos un administrador activo.");
    }
}

// ---------- Consultas ----------

[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record ObtenerUsuariosQuery : IRequest<IReadOnlyList<UsuarioDto>>;

public sealed class ObtenerUsuariosQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerUsuariosQuery, IReadOnlyList<UsuarioDto>>
{
    public async Task<IReadOnlyList<UsuarioDto>> Handle(ObtenerUsuariosQuery request, CancellationToken ct) =>
        await GestionUsuarios.Proyectar(db.Usuarios.AsNoTracking()
                .OrderByDescending(u => u.EstaActivo).ThenBy(u => u.NombreCompleto))
            .ToListAsync(ct);
}

[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record ObtenerUsuarioPorIdQuery(Guid Id) : IRequest<UsuarioDto>;

public sealed class ObtenerUsuarioPorIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerUsuarioPorIdQuery, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(ObtenerUsuarioPorIdQuery request, CancellationToken ct) =>
        await GestionUsuarios.Proyectar(db.Usuarios.AsNoTracking().Where(u => u.Id == request.Id)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("el usuario", request.Id);
}

// ---------- Alta ----------

[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record CrearUsuarioCommand(
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    string Password,
    Guid RolId) : IRequest<UsuarioDto>;

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
        RuleFor(x => x.RolId).NotEmpty().WithMessage("Elegí el rol del usuario.");
    }
}

public sealed class CrearUsuarioCommandHandler(
    IApplicationDbContext db, IPasswordHasher passwordHasher, ICurrentUserService usuarioActual, RegistradorAuditoria auditoria,
    SuscripcionActual suscripcion)
    : IRequestHandler<CrearUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(CrearUsuarioCommand request, CancellationToken ct)
    {
        var rol = await GestionUsuarios.ObtenerRolAsignableAsync(db, request.RolId, ct);
        ReglasRoles.ExigirPuedeAdministrar(usuarioActual, rol, "asignar el rol");
        await ReglasPlan.ExigirCupoUsuariosAsync(db, suscripcion, ct);

        var username = Usuario.NormalizarUsername(request.NombreUsuario);
        if (await db.Usuarios.AnyAsync(u => u.Username == username, ct))
            throw new ConflictException($"Ya existe el usuario '{username}' en este comercio.");

        var usuario = Usuario.Crear(request.NombreCompleto, username, passwordHasher.Hash(request.Password), rol, request.Email);
        db.Usuarios.Add(usuario);
        auditoria.Registrar(AccionesAuditoria.UsuarioCreado, nameof(Usuario), usuario.Id,
            $"Usuario '{usuario.Username}' ({usuario.NombreCompleto}) con el rol '{rol.Nombre}'.");
        await db.SaveChangesAsync(ct); // el índice único (TenantId, Username) cubre la carrera entre dos altas

        return GestionUsuarios.ToDto(usuario, rol);
    }
}

// ---------- Edición ----------

[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record ActualizarUsuarioCommand(Guid Id, string NombreCompleto, string? Email, Guid RolId) : IRequest<UsuarioDto>;

public sealed class ActualizarUsuarioCommandValidator : AbstractValidator<ActualizarUsuarioCommand>
{
    public ActualizarUsuarioCommandValidator()
    {
        RuleFor(x => x.NombreCompleto).NotEmpty().MaximumLength(Usuario.LongitudMaximaNombre);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(Usuario.LongitudMaximaEmail)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.RolId).NotEmpty().WithMessage("Elegí el rol del usuario.");
    }
}

public sealed class ActualizarUsuarioCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual, RegistradorAuditoria auditoria)
    : IRequestHandler<ActualizarUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(ActualizarUsuarioCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);
        var rolAnterior = usuario.Rol!;
        ReglasRoles.ExigirPuedeAdministrar(usuarioActual, rolAnterior, "modificar usuarios con el rol");

        var rolNuevo = rolAnterior;
        if (request.RolId != usuario.RolId)
        {
            rolNuevo = await GestionUsuarios.ObtenerRolAsignableAsync(db, request.RolId, ct);
            ReglasRoles.ExigirPuedeAdministrar(usuarioActual, rolNuevo, "asignar el rol");
            if (rolAnterior.EsAdministrador && !rolNuevo.EsAdministrador)
                await GestionUsuarios.ExigirOtroAdministradorActivoAsync(db, usuario.Id, ct);
        }

        var datosCambiaron = usuario.NombreCompleto != request.NombreCompleto.Trim()
                             || usuario.Email != (string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant());
        usuario.ActualizarDatos(request.NombreCompleto, request.Email);
        if (datosCambiaron)
            auditoria.Registrar(AccionesAuditoria.UsuarioModificado, nameof(Usuario), usuario.Id, $"Datos de '{usuario.Username}' actualizados.");

        if (rolNuevo.Id != rolAnterior.Id)
        {
            usuario.CambiarRol(rolNuevo); // renueva el sello: el usuario vuelve a ingresar
            auditoria.Registrar(AccionesAuditoria.UsuarioRolCambiado, nameof(Usuario), usuario.Id,
                $"'{usuario.Username}': de '{rolAnterior.Nombre}' a '{rolNuevo.Nombre}'.");
        }

        await db.SaveChangesAsync(ct);
        return GestionUsuarios.ToDto(usuario, rolNuevo);
    }
}

// ---------- Contraseña ----------

/// <summary>Reseteo de contraseña por un administrador de usuarios. Revoca los tokens vigentes del usuario.</summary>
[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record CambiarPasswordCommand(Guid Id, string NuevaPassword) : IRequest;

public sealed class CambiarPasswordCommandValidator : AbstractValidator<CambiarPasswordCommand>
{
    public CambiarPasswordCommandValidator() => RuleFor(x => x.NuevaPassword).PasswordSegura();
}

public sealed class CambiarPasswordCommandHandler(
    IApplicationDbContext db, IPasswordHasher passwordHasher, ICurrentUserService usuarioActual, RegistradorAuditoria auditoria)
    : IRequestHandler<CambiarPasswordCommand>
{
    public async Task Handle(CambiarPasswordCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);
        ReglasRoles.ExigirPuedeAdministrar(usuarioActual, usuario.Rol!, "resetear la contraseña de usuarios con el rol");

        usuario.CambiarPasswordHash(passwordHasher.Hash(request.NuevaPassword));
        auditoria.Registrar(AccionesAuditoria.UsuarioPasswordReseteada, nameof(Usuario), usuario.Id,
            $"Contraseña de '{usuario.Username}' reseteada.");
        await db.SaveChangesAsync(ct);
    }
}

// ---------- Estado ----------

/// <summary>Activación / baja lógica. Al desactivar, los tokens vigentes del usuario dejan de servir.</summary>
[RequierePermiso(Permisos.UsuariosGestionar)]
public sealed record CambiarEstadoUsuarioCommand(Guid Id, bool Activo) : IRequest<UsuarioDto>;

public sealed class CambiarEstadoUsuarioCommandHandler(
    IApplicationDbContext db, ICurrentUserService usuarioActual, RegistradorAuditoria auditoria, SuscripcionActual suscripcion)
    : IRequestHandler<CambiarEstadoUsuarioCommand, UsuarioDto>
{
    public async Task<UsuarioDto> Handle(CambiarEstadoUsuarioCommand request, CancellationToken ct)
    {
        var usuario = await GestionUsuarios.ObtenerAsync(db, request.Id, ct);
        var rol = usuario.Rol!;
        ReglasRoles.ExigirPuedeAdministrar(usuarioActual, rol, request.Activo ? "activar usuarios con el rol" : "dar de baja usuarios con el rol");

        if (request.Activo)
        {
            if (!rol.EstaActivo)
                throw new DomainException($"El rol '{rol.Nombre}' fue eliminado: asignale otro rol al usuario antes de activarlo.");
            if (!usuario.EstaActivo)
                await ReglasPlan.ExigirCupoUsuariosAsync(db, suscripcion, ct);
            if (!usuario.EstaActivo)
                auditoria.Registrar(AccionesAuditoria.UsuarioActivado, nameof(Usuario), usuario.Id, $"Usuario '{usuario.Username}' activado.");
            usuario.Activar();
        }
        else
        {
            var solicitanteId = usuarioActual.UsuarioId
                ?? throw new AccesoDenegadoException("No hay un usuario autenticado.");
            var estabaActivo = usuario.EstaActivo;
            usuario.Desactivar(solicitanteId); // regla de dominio: nadie se desactiva a sí mismo

            if (rol.EsAdministrador)
                await GestionUsuarios.ExigirOtroAdministradorActivoAsync(db, usuario.Id, ct);
            if (estabaActivo)
                auditoria.Registrar(AccionesAuditoria.UsuarioDesactivado, nameof(Usuario), usuario.Id, $"Usuario '{usuario.Username}' dado de baja.");
        }

        await db.SaveChangesAsync(ct);
        return GestionUsuarios.ToDto(usuario, rol);
    }
}
