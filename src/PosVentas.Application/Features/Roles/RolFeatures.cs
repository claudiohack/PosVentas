using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Auditoria;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Seguridad;

namespace PosVentas.Application.Features.Roles;

// Roles y permisos configurables por comercio. Reglas de seguridad:
//  - Nadie otorga permisos que no tiene, ni edita o elimina un rol con permisos que no tiene (sin escalada).
//  - El Administrador es inmutable; los roles de sistema no se eliminan ni se renombran.
//  - Un cambio de permisos rige en el próximo request de cada usuario del rol (se leen de la base en cada request).

public sealed record RolDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? CodigoSistema,
    bool EsSistema,
    bool EsAdministrador,
    bool Activo,
    IReadOnlyList<string> Permisos,
    int CantidadUsuarios);

internal static class RolMapper
{
    public static RolDto ToDto(Rol r, int cantidadUsuarios) => new(
        r.Id, r.Nombre, r.Descripcion, r.CodigoSistema, r.EsSistema, r.EsAdministrador, r.EstaActivo,
        CatalogoPermisos.Ordenar(r.PermisosEfectivos()), cantidadUsuarios);
}

internal static class ReglasRoles
{
    public static async Task<Rol> ObtenerAsync(IApplicationDbContext db, Guid id, CancellationToken ct) =>
        await db.Roles.Include(r => r.Permisos).SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("el rol", id);

    /// <summary>Sin escalada de privilegios: nadie otorga permisos que su propio rol no tiene.</summary>
    public static void ExigirPermisosPropios(ICurrentUserService usuario, IEnumerable<string> permisos)
    {
        var ajenos = Ajenos(usuario, permisos);
        if (ajenos.Count > 0)
            throw new AccesoDenegadoException($"No podés otorgar permisos que tu rol no tiene: {string.Join(", ", ajenos)}.");
    }

    /// <summary>
    /// Sin escalada de privilegios: quien no es Administrador no administra un rol (ni a sus usuarios)
    /// con permisos que él no tiene. <paramref name="accion"/> completa la frase, p. ej. "asignar el rol".
    /// </summary>
    public static void ExigirPuedeAdministrar(ICurrentUserService usuario, Rol rol, string accion)
    {
        if (usuario.EsAdministrador)
            return;
        if (rol.EsAdministrador)
            throw new AccesoDenegadoException($"Solo un Administrador puede {accion} '{rol.Nombre}'.");
        var ajenos = Ajenos(usuario, rol.PermisosEfectivos());
        if (ajenos.Count > 0)
            throw new AccesoDenegadoException($"No podés {accion} '{rol.Nombre}': tiene permisos que tu rol no tiene ({string.Join(", ", ajenos)}).");
    }

    private static List<string> Ajenos(ICurrentUserService usuario, IEnumerable<string> permisos) =>
        usuario.EsAdministrador ? [] : CatalogoPermisos.Ordenar(permisos.Where(p => !usuario.TienePermiso(p))).Select(CatalogoPermisos.NombreDe).ToList();

    public static async Task ExigirNombreLibreAsync(IApplicationDbContext db, string nombre, Guid? exceptoId, CancellationToken ct)
    {
        var normalizado = Rol.NormalizarNombre(nombre);
        if (await db.Roles.AnyAsync(r => r.EstaActivo && r.Nombre == normalizado && r.Id != exceptoId, ct))
            throw new ConflictException($"Ya existe un rol llamado '{normalizado}'.");
    }

    public static string DescribirCambios(IReadOnlyCollection<string> antes, IReadOnlyCollection<string> despues)
    {
        var agregados = CatalogoPermisos.Ordenar(despues.Except(antes)).Select(CatalogoPermisos.NombreDe).ToList();
        var quitados = CatalogoPermisos.Ordenar(antes.Except(despues)).Select(CatalogoPermisos.NombreDe).ToList();
        var partes = new List<string>();
        if (agregados.Count > 0) partes.Add("Agregados: " + string.Join(", ", agregados));
        if (quitados.Count > 0) partes.Add("Quitados: " + string.Join(", ", quitados));
        return string.Join(". ", partes);
    }
}

public abstract class DatosRolValidator<T> : AbstractValidator<T>
{
    protected void ValidarDatos(Func<T, string> nombre, Func<T, string?> descripcion, Func<T, IReadOnlyList<string>?> permisos)
    {
        RuleFor(x => nombre(x)).NotEmpty().WithName("Nombre")
            .Must(n => Rol.NormalizarNombre(n).Length is >= Rol.LongitudMinimaNombre and <= Rol.LongitudMaximaNombre)
            .WithMessage($"El nombre del rol debe tener entre {Rol.LongitudMinimaNombre} y {Rol.LongitudMaximaNombre} caracteres.");
        RuleFor(x => descripcion(x)).MaximumLength(Rol.LongitudMaximaDescripcion).WithName("Descripción");
        RuleFor(x => permisos(x)).NotEmpty().WithName("Permisos").WithMessage("Elegí al menos un permiso.");
        RuleForEach(x => permisos(x)).Must(Permisos.Existe).WithName("Permisos").WithMessage("El permiso '{PropertyValue}' no existe.");
    }
}

// ---------- Consultas ----------

[RequierePermiso(Permisos.RolesGestionar)]
public sealed record ObtenerCatalogoPermisosQuery : IRequest<IReadOnlyList<GrupoPermisosDto>>;

public sealed class ObtenerCatalogoPermisosQueryHandler : IRequestHandler<ObtenerCatalogoPermisosQuery, IReadOnlyList<GrupoPermisosDto>>
{
    public Task<IReadOnlyList<GrupoPermisosDto>> Handle(ObtenerCatalogoPermisosQuery request, CancellationToken ct) =>
        Task.FromResult(CatalogoPermisos.Grupos);
}

/// <summary>Roles del comercio. La usan la gestión de roles y la de usuarios (para elegir el rol de cada uno).</summary>
public sealed record ObtenerRolesQuery(bool IncluirEliminados = false) : IRequest<IReadOnlyList<RolDto>>;

public sealed class ObtenerRolesQueryHandler(IApplicationDbContext db, ICurrentUserService usuario)
    : IRequestHandler<ObtenerRolesQuery, IReadOnlyList<RolDto>>
{
    public async Task<IReadOnlyList<RolDto>> Handle(ObtenerRolesQuery request, CancellationToken ct)
    {
        if (!usuario.TienePermiso(Permisos.RolesGestionar) && !usuario.TienePermiso(Permisos.UsuariosGestionar))
            throw new AccesoDenegadoException("Tu rol no puede consultar los roles.");

        var roles = await db.Roles.AsNoTracking().Include(r => r.Permisos)
            .Where(r => request.IncluirEliminados || r.EstaActivo)
            .ToListAsync(ct);
        var usuariosPorRol = await db.Usuarios.AsNoTracking()
            .GroupBy(u => u.RolId).Select(g => new { RolId = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.RolId, x => x.Cantidad, ct);

        return roles
            .OrderByDescending(r => r.EstaActivo).ThenBy(r => r.EsSistema ? 0 : 1)
            .ThenBy(r => r.CodigoSistema switch { RolesSistema.Administrador => 0, RolesSistema.Supervisor => 1, _ => 2 })
            .ThenBy(r => r.Nombre)
            .Select(r => RolMapper.ToDto(r, usuariosPorRol.GetValueOrDefault(r.Id)))
            .ToList();
    }
}

[RequierePermiso(Permisos.RolesGestionar)]
public sealed record ObtenerRolPorIdQuery(Guid Id) : IRequest<RolDto>;

public sealed class ObtenerRolPorIdQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerRolPorIdQuery, RolDto>
{
    public async Task<RolDto> Handle(ObtenerRolPorIdQuery request, CancellationToken ct)
    {
        var rol = await ReglasRoles.ObtenerAsync(db, request.Id, ct);
        return RolMapper.ToDto(rol, await db.Usuarios.CountAsync(u => u.RolId == rol.Id, ct));
    }
}

// ---------- Alta ----------

[RequierePermiso(Domain.Seguridad.Permisos.RolesGestionar)] // calificado: el record tiene una propiedad Permisos
public sealed record CrearRolCommand(string Nombre, string? Descripcion, IReadOnlyList<string> Permisos) : IRequest<RolDto>;

public sealed class CrearRolCommandValidator : DatosRolValidator<CrearRolCommand>
{
    public CrearRolCommandValidator() => ValidarDatos(x => x.Nombre, x => x.Descripcion, x => x.Permisos);
}

public sealed class CrearRolCommandHandler(IApplicationDbContext db, ICurrentUserService usuario, RegistradorAuditoria auditoria)
    : IRequestHandler<CrearRolCommand, RolDto>
{
    public async Task<RolDto> Handle(CrearRolCommand request, CancellationToken ct)
    {
        ReglasRoles.ExigirPermisosPropios(usuario, request.Permisos);
        await ReglasRoles.ExigirNombreLibreAsync(db, request.Nombre, null, ct);

        var rol = Rol.CrearPersonalizado(request.Nombre, request.Descripcion, request.Permisos);
        db.Roles.Add(rol);
        auditoria.Registrar(AccionesAuditoria.RolCreado, nameof(Rol), rol.Id,
            $"Rol '{rol.Nombre}' con permisos: {string.Join(", ", CatalogoPermisos.Ordenar(rol.PermisosEfectivos()).Select(CatalogoPermisos.NombreDe))}.");
        await db.SaveChangesAsync(ct); // índice único filtrado (TenantId, Nombre) de roles activos cubre la carrera

        return RolMapper.ToDto(rol, 0);
    }
}

// ---------- Edición ----------

[RequierePermiso(Domain.Seguridad.Permisos.RolesGestionar)]
public sealed record ActualizarRolCommand(Guid Id, string Nombre, string? Descripcion, IReadOnlyList<string> Permisos) : IRequest<RolDto>;

public sealed class ActualizarRolCommandValidator : DatosRolValidator<ActualizarRolCommand>
{
    public ActualizarRolCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ValidarDatos(x => x.Nombre, x => x.Descripcion, x => x.Permisos);
    }
}

public sealed class ActualizarRolCommandHandler(IApplicationDbContext db, ICurrentUserService usuario, RegistradorAuditoria auditoria)
    : IRequestHandler<ActualizarRolCommand, RolDto>
{
    public async Task<RolDto> Handle(ActualizarRolCommand request, CancellationToken ct)
    {
        var rol = await ReglasRoles.ObtenerAsync(db, request.Id, ct);
        if (!rol.EstaActivo)
            throw new NotFoundException("el rol", request.Id);
        if (rol.EsAdministrador)
            throw new Domain.Common.DomainException("El rol Administrador tiene todos los permisos y no se puede modificar.");

        ReglasRoles.ExigirPuedeAdministrar(usuario, rol, "modificar el rol");
        ReglasRoles.ExigirPermisosPropios(usuario, request.Permisos);
        await ReglasRoles.ExigirNombreLibreAsync(db, request.Nombre, rol.Id, ct);

        var antes = rol.PermisosEfectivos();
        var nombreAnterior = rol.Nombre;
        rol.Modificar(request.Nombre, request.Descripcion);
        var cambiaronPermisos = rol.AsignarPermisos(request.Permisos);

        var detalle = new List<string>();
        if (nombreAnterior != rol.Nombre) detalle.Add($"Renombrado de '{nombreAnterior}' a '{rol.Nombre}'");
        if (cambiaronPermisos) detalle.Add(ReglasRoles.DescribirCambios(antes, rol.PermisosEfectivos()));
        auditoria.Registrar(AccionesAuditoria.RolModificado, nameof(Rol), rol.Id,
            $"Rol '{rol.Nombre}'. " + (detalle.Count > 0 ? string.Join(". ", detalle) + "." : "Datos actualizados."));

        await db.SaveChangesAsync(ct); // RowVersion: dos ediciones simultáneas → 409
        return RolMapper.ToDto(rol, await db.Usuarios.CountAsync(u => u.RolId == rol.Id, ct));
    }
}

// ---------- Baja ----------

/// <summary>Baja lógica: el rol deja de listarse y de poder asignarse. Solo si no tiene usuarios.</summary>
[RequierePermiso(Permisos.RolesGestionar)]
public sealed record EliminarRolCommand(Guid Id) : IRequest;

public sealed class EliminarRolCommandHandler(IApplicationDbContext db, ICurrentUserService usuario, RegistradorAuditoria auditoria)
    : IRequestHandler<EliminarRolCommand>
{
    public async Task Handle(EliminarRolCommand request, CancellationToken ct)
    {
        var rol = await ReglasRoles.ObtenerAsync(db, request.Id, ct);
        if (!rol.EstaActivo)
            throw new NotFoundException("el rol", request.Id);

        ReglasRoles.ExigirPuedeAdministrar(usuario, rol, "eliminar el rol");
        if (rol.EsSistema)
            throw new Domain.Common.DomainException("Los roles predeterminados no se pueden eliminar.");
        var usuarios = await db.Usuarios.CountAsync(u => u.RolId == rol.Id, ct);
        if (usuarios > 0)
            throw new ConflictException($"El rol '{rol.Nombre}' tiene {usuarios} usuario{(usuarios == 1 ? "" : "s")}: asignales otro rol antes de eliminarlo.");

        rol.Desactivar(); // regla de dominio: los de sistema no se eliminan
        auditoria.Registrar(AccionesAuditoria.RolEliminado, nameof(Rol), rol.Id, $"Rol '{rol.Nombre}' eliminado.");
        await db.SaveChangesAsync(ct);
    }
}
