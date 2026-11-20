using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Categorias;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/categorias")]
[Authorize(Policy = Politicas.Operador)]
public sealed class CategoriasController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoriaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoriaDto>>> Listar([FromQuery] bool incluirInactivas, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCategoriasQuery(incluirInactivas), ct));

    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(CrearCategoriaCommand command, CancellationToken ct)
    {
        var id = await mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }
}
