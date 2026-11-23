using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Auth;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Sesión vigente del usuario (rol y permisos según la base). Fuera de AuthController a propósito: no comparte
/// su límite de intentos de login, porque el cliente la consulta al abrir la aplicación.
/// </summary>
[ApiController]
[Route("api/auth/sesion")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class SesionController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SesionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SesionDto>> Obtener(CancellationToken ct) => Ok(await mediator.Send(new ObtenerSesionQuery(), ct));
}
