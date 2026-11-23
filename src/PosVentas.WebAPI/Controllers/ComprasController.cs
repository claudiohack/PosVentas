using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Compras;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>Compras de mercadería con ingreso de stock. Administradores y supervisores.</summary>
[ApiController]
[Route("api/compras")]
public sealed class ComprasController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [AutorizarPermiso(Permisos.ComprasConsultar)]
    [ProducesResponseType<IReadOnlyList<CompraResumenDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CompraResumenDto>>> Listar(
        [FromQuery] Guid? proveedorId, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerComprasQuery(proveedorId, desde, hasta), ct));

    [HttpGet("{id:guid}")]
    [AutorizarPermiso(Permisos.ComprasConsultar)]
    [ProducesResponseType<CompraDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompraDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCompraPorIdQuery(id), ct));

    /// <summary>
    /// Registra la compra en una sola transacción: ingresa el stock, actualiza el costo (promedio ponderado)
    /// y, si se pagó al contado con una caja abierta, registra el egreso en el turno.
    /// </summary>
    [HttpPost]
    [AutorizarPermiso(Permisos.ComprasRegistrar)]
    [ProducesResponseType<CompraDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CompraDto>> Registrar(RegistrarCompraCommand command, CancellationToken ct)
    {
        var compra = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Obtener), new { id = compra.Id }, compra);
    }
}
