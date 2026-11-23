using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Usuarios;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Gestión de usuarios del comercio (permiso usuarios.gestionar). Un usuario de otro comercio responde 404.
/// Quien no es Administrador no administra usuarios de roles con permisos que él no tiene (403).
/// </summary>
[ApiController]
[Route("api/usuarios")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class UsuariosController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType<IReadOnlyList<UsuarioDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UsuarioDto>>> Listar(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerUsuariosQuery(), ct));

    [HttpGet("{id:guid}")]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsuarioDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerUsuarioPorIdQuery(id), ct));

    [HttpPost]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioDto>> Crear(CrearUsuarioCommand command, CancellationToken ct)
    {
        var usuario = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Obtener), new { id = usuario.Id }, usuario);
    }

    /// <summary>Edita nombre, email y rol. Cambiar el rol revoca las sesiones abiertas del usuario.</summary>
    [HttpPut("{id:guid}")]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UsuarioDto>> Actualizar(Guid id, ActualizarUsuarioRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarUsuarioCommand(id, request.NombreCompleto, request.Email, request.RolId), ct));

    /// <summary>Resetea la contraseña. Revoca las sesiones abiertas del usuario.</summary>
    [HttpPut("{id:guid}/password")]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CambiarPassword(Guid id, CambiarPasswordRequest request, CancellationToken ct)
    {
        await mediator.Send(new CambiarPasswordCommand(id, request.NuevaPassword), ct);
        return NoContent();
    }

    /// <summary>Activa o desactiva (baja lógica). Nadie puede desactivarse a sí mismo.</summary>
    [HttpPatch("{id:guid}/estado")]
    [AutorizarPermiso(Permisos.UsuariosGestionar)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UsuarioDto>> CambiarEstado(Guid id, CambiarEstadoRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new CambiarEstadoUsuarioCommand(id, request.Activo), ct));

    public sealed record ActualizarUsuarioRequest(string NombreCompleto, string? Email, Guid RolId);
    public sealed record CambiarPasswordRequest(string NuevaPassword);
    public sealed record CambiarEstadoRequest(bool Activo);
}
