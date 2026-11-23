using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Roles;

namespace PosVentas.Application.Features.Auth;

/// <summary>Sesión del usuario autenticado según la base: rol y permisos vigentes (los cambios de un rol se ven sin reingresar).</summary>
public sealed record ObtenerSesionQuery : IRequest<SesionDto>;

public sealed class ObtenerSesionQueryHandler(IApplicationDbContext db, ICurrentUserService usuarioActual, ICurrentTenantService tenant)
    : IRequestHandler<ObtenerSesionQuery, SesionDto>
{
    public async Task<SesionDto> Handle(ObtenerSesionQuery request, CancellationToken ct)
    {
        var usuarioId = usuarioActual.UsuarioId ?? throw new AccesoDenegadoException("No hay un usuario autenticado.");
        var usuario = await db.Usuarios.AsNoTracking().Include(u => u.Rol).ThenInclude(r => r!.Permisos)
                          .SingleOrDefaultAsync(u => u.Id == usuarioId, ct)
                      ?? throw new NotFoundException("el usuario", usuarioId);
        var comercio = await db.Comercios.AsNoTracking().SingleAsync(c => c.Id == tenant.TenantId, ct);
        var rol = usuario.Rol!;

        return new SesionDto(usuario.Id, usuario.NombreCompleto, rol.Id, rol.Nombre, rol.EsAdministrador,
            CatalogoPermisos.Ordenar(rol.PermisosEfectivos()), comercio.Id, comercio.Nombre, usuarioActual.EsAdministradorPlataforma);
    }
}
