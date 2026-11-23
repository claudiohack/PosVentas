using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Anulaciones;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/ventas")]
public sealed class VentasController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Registra y cobra una venta en un turno de caja abierto. El vendedor se toma del JWT.
    /// Con Factura A/B/C pide además el CAE a ARCA; si ARCA rechaza (400) o no responde (503),
    /// la venta queda registrada como pendiente de facturación (el id viene en "ventaId").
    /// </summary>
    [HttpPost]
    [AutorizarPermiso(Permisos.VentasRegistrar)]
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
    [AutorizarPermiso(Permisos.VentasConsultar)]
    [ProducesResponseType<VentaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VentaDto>> ObtenerPorId(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerVentaPorIdQuery(id), ct));

    /// <summary>
    /// Factura electrónicamente una venta emitida como ticket interno, o reintenta una factura pendiente.
    /// 400 con los códigos de ARCA si la rechaza; 503 si ARCA no responde (la venta sigue pendiente).
    /// </summary>
    [HttpPost("{id:guid}/facturar")]
    [AutorizarPermiso(Permisos.VentasRegistrar)]
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
    [AutorizarPermiso(Permisos.VentasConsultar)]
    [ProducesResponseType<ComprobanteImpresionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComprobanteImpresionDto>> Comprobante(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerComprobanteQuery(id), ct));

    /// <summary>Historial de ventas del período (fechas yyyy-MM-dd, calendario argentino; sin fechas = hoy).</summary>
    [HttpGet]
    [AutorizarPermiso(Permisos.VentasConsultar)]
    [ProducesResponseType<IReadOnlyList<VentaHistorialDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<VentaHistorialDto>>> Historial(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] EstadoVenta? estado, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerHistorialVentasQuery(desde, hasta, estado), ct));

    /// <summary>
    /// Anula la venta en una sola transacción: reintegra el stock, devuelve el dinero en la caja abierta
    /// y acredita la cuenta corriente. Si estaba facturada, pide además la nota de crédito a ARCA
    /// (si ARCA la rechaza: 400; si no responde: 503; en ambos casos la anulación ya quedó hecha).
    /// </summary>
    [HttpPost("{id:guid}/anular")]
    [AutorizarPermiso(Permisos.VentasAnular)]
    [ProducesResponseType<AnulacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AnulacionDto>> Anular(Guid id, AnularRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new AnularVentaCommand(id, request.MotivoAnulacion, request.CajaSesionId), ct));

    [HttpGet("{id:guid}/anulacion")]
    [AutorizarPermiso(Permisos.VentasConsultar)]
    [ProducesResponseType<AnulacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnulacionDto>> Anulacion(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerAnulacionQuery(id), ct));

    /// <summary>Reintenta la nota de crédito pendiente de una venta anulada.</summary>
    [HttpPost("{id:guid}/nota-credito")]
    [AutorizarPermiso(Permisos.VentasAnular)]
    [ProducesResponseType<AnulacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AnulacionDto>> EmitirNotaCredito(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new EmitirNotaCreditoPendienteCommand(id), ct));

    /// <summary>Datos para imprimir la nota de crédito (CAE y QR de ARCA) o el comprobante interno de anulación.</summary>
    [HttpGet("{id:guid}/nota-credito")]
    [AutorizarPermiso(Permisos.VentasConsultar)]
    [ProducesResponseType<ComprobanteImpresionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComprobanteImpresionDto>> NotaCredito(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerNotaCreditoQuery(id), ct));

    public sealed record FacturarRequest(TipoComprobante TipoComprobante);
    public sealed record AnularRequest(string MotivoAnulacion, Guid? CajaSesionId = null);
}
