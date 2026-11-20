using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PosVentas.Application.Features.Auth;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(ISender mediator) : ControllerBase
{
    /// <summary>Alta de un comercio nuevo con su usuario administrador. Devuelve un JWT listo para usar.</summary>
    [HttpPost("registro")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Registrar(RegistrarComercioCommand command, CancellationToken ct)
    {
        var respuesta = await mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, respuesta);
    }

    /// <summary>Login con identificador de comercio, usuario y contraseña. Devuelve un JWT con uid, tid y role.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));
}
