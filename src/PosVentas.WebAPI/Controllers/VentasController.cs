using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Ventas;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/ventas")]
[Authorize(Policy = Politicas.Operador)]
public sealed class VentasController(ISender mediator) : ControllerBase
{
    /// <summary>Registra y cobra una venta en un turno de caja abierto. El vendedor se toma del JWT.</summary>
    [HttpPost]
    [ProducesResponseType<VentaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VentaDto>> Registrar(RegistrarVentaCommand command, CancellationToken ct)
    {
        var venta = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = venta.Id }, venta);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<VentaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VentaDto>> ObtenerPorId(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerVentaPorIdQuery(id), ct));
}
