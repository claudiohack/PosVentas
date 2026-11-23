using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Auth;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Autenticación y alta de comercios. El alta es SOLO por invitación: un administrador de la plataforma (HS Tech)
/// emite una invitación de un solo uso y el comercio se registra con ese token.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(ISender mediator) : ControllerBase
{
    /// <summary>Login con identificador de comercio, usuario y contraseña. Devuelve un JWT con uid, tid y role.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    /// <summary>Alta de un comercio con una invitación vigente. Devuelve el JWT del administrador creado.</summary>
    [HttpPost("registro-invitacion")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> RegistrarConInvitacion(RegistrarComercioCommand command, CancellationToken ct)
    {
        var respuesta = await mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, respuesta);
    }

    /// <summary>
    /// El alta abierta de comercios está deshabilitada: sin token responde 401, con un token común 403
    /// y a un administrador de la plataforma le indica el flujo de invitaciones (410).
    /// </summary>
    [HttpPost("registro")]
    [Authorize(Policy = Politicas.AdministradorGlobal)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult RegistroDeshabilitado() => Problem(
        statusCode: StatusCodes.Status410Gone,
        title: "Alta directa deshabilitada.",
        detail: "Emití una invitación con POST /api/auth/invitaciones y registrá el comercio con POST /api/auth/registro-invitacion.");

    // ---------- Invitaciones (solo administradores de la plataforma) ----------

    /// <summary>Emite una invitación de un solo uso. El token se devuelve UNA vez: después solo queda su hash.</summary>
    [HttpPost("invitaciones")]
    [Authorize(Policy = Politicas.AdministradorGlobal)]
    [ProducesResponseType<InvitacionCreadaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InvitacionCreadaDto>> CrearInvitacion(CrearInvitacionCommand command, CancellationToken ct)
    {
        var invitacion = await mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, invitacion);
    }

    [HttpGet("invitaciones")]
    [Authorize(Policy = Politicas.AdministradorGlobal)]
    [ProducesResponseType<IReadOnlyList<InvitacionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InvitacionDto>>> ListarInvitaciones(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerInvitacionesQuery(), ct));

    [HttpDelete("invitaciones/{id:guid}")]
    [Authorize(Policy = Politicas.AdministradorGlobal)]
    [ProducesResponseType<InvitacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InvitacionDto>> RevocarInvitacion(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new RevocarInvitacionCommand(id), ct));
}
