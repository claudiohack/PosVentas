using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Roles;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Roles del comercio y sus permisos. Un rol de otro comercio responde 404. Los cambios de permisos rigen
/// en el próximo request de cada usuario del rol, sin necesidad de volver a ingresar.
/// </summary>
[ApiController]
[Route("api/roles")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class RolesController(ISender mediator) : ControllerBase
{
    /// <summary>Roles activos (o todos). También lo usa la gestión de usuarios para elegir el rol.</summary>
    [HttpGet]
    [AutorizarPermiso(Permisos.RolesGestionar, Permisos.UsuariosGestionar)]
    [ProducesResponseType<IReadOnlyList<RolDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RolDto>>> Listar([FromQuery] bool incluirEliminados, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerRolesQuery(incluirEliminados), ct));

    /// <summary>Catálogo de permisos disponibles, agrupados por módulo.</summary>
    [HttpGet("permisos")]
    [AutorizarPermiso(Permisos.RolesGestionar)]
    [ProducesResponseType<IReadOnlyList<GrupoPermisosDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GrupoPermisosDto>>> Permisos_(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCatalogoPermisosQuery(), ct));

    [HttpGet("{id:guid}")]
    [AutorizarPermiso(Permisos.RolesGestionar)]
    [ProducesResponseType<RolDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RolDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerRolPorIdQuery(id), ct));

    [HttpPost]
    [AutorizarPermiso(Permisos.RolesGestionar)]
    [ProducesResponseType<RolDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RolDto>> Crear(CrearRolCommand command, CancellationToken ct)
    {
        var rol = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Obtener), new { id = rol.Id }, rol);
    }

    /// <summary>Reemplaza nombre, descripción y el conjunto completo de permisos. El Administrador no se modifica (422).</summary>
    [HttpPut("{id:guid}")]
    [AutorizarPermiso(Permisos.RolesGestionar)]
    [ProducesResponseType<RolDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RolDto>> Actualizar(Guid id, ActualizarRolRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarRolCommand(id, r.Nombre, r.Descripcion, r.Permisos), ct));

    /// <summary>Baja lógica. Los roles predeterminados no se eliminan (422); uno con usuarios tampoco (409).</summary>
    [HttpDelete("{id:guid}")]
    [AutorizarPermiso(Permisos.RolesGestionar)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await mediator.Send(new EliminarRolCommand(id), ct);
        return NoContent();
    }

    public sealed record ActualizarRolRequest(string Nombre, string? Descripcion, IReadOnlyList<string> Permisos);
}
