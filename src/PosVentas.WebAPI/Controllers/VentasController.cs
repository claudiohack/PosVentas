using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/ventas")]
[Authorize(Policy = Politicas.Operador)]
public sealed class VentasController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Registra y cobra una venta en un turno de caja abierto. El vendedor se toma del JWT.
    /// Con Factura A/B/C pide además el CAE a ARCA; si ARCA rechaza (400) o no responde (503),
    /// la venta queda registrada como pendiente de facturación (el id viene en "ventaId").
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

    /// <summary>
    /// Factura electrónicamente una venta emitida como ticket interno, o reintenta una factura pendiente.
    /// 400 con los códigos de ARCA si la rechaza; 503 si ARCA no responde (la venta sigue pendiente).
    /// </summary>
    [HttpPost("{id:guid}/facturar")]
    [ProducesResponseType<VentaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<VentaDto>> Facturar(Guid id, FacturarRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new FacturarVentaCommand(id, request.TipoComprobante), ct));

    /// <summary>Datos para imprimir el comprobante: emisor, receptor, renglones, totales, CAE y QR fiscal (RG 4892).</summary>
    [HttpGet("{id:guid}/comprobante")]
    [ProducesResponseType<ComprobanteImpresionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComprobanteImpresionDto>> Comprobante(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerComprobanteQuery(id), ct));

    public sealed record FacturarRequest(TipoComprobante TipoComprobante);
}
