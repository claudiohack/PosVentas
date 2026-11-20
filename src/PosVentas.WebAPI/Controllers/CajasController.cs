using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Ventas;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/cajas")]
[Authorize(Policy = Politicas.Operador)]
public sealed class CajasController(ISender mediator) : ControllerBase
{
    /// <summary>Cajas del comercio con su turno abierto (si lo hay).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CajaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CajaDto>>> Listar(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCajasQuery(), ct));

    /// <summary>Abre un turno con el fondo inicial indicado.</summary>
    [HttpPost("{cajaId:guid}/apertura")]
    [ProducesResponseType<SesionCajaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SesionCajaDto>> Abrir(Guid cajaId, AperturaRequest request, CancellationToken ct)
    {
        var sesion = await mediator.Send(new AbrirCajaCommand(cajaId, request.MontoApertura), ct);
        return CreatedAtAction(nameof(Resumen), new { sesionId = sesion.Id }, sesion);
    }

    /// <summary>Resumen del turno: ventas, totales por medio de pago y efectivo esperado.</summary>
    [HttpGet("sesiones/{sesionId:guid}/resumen")]
    [ProducesResponseType<ResumenSesionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumenSesionDto>> Resumen(Guid sesionId, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerResumenSesionQuery(sesionId), ct));

    /// <summary>Ventas registradas en el turno.</summary>
    [HttpGet("sesiones/{sesionId:guid}/ventas")]
    [ProducesResponseType<IReadOnlyList<VentaResumenDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VentaResumenDto>>> Ventas(Guid sesionId, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerVentasDeSesionQuery(sesionId), ct));

    /// <summary>
    /// Cierra el turno con el efectivo contado y calcula el arqueo (sobrante / faltante).
    /// Solo administradores y supervisores: el cajero no cierra su propia caja.
    /// </summary>
    [HttpPost("sesiones/{sesionId:guid}/cierre")]
    [Authorize(Policy = Politicas.Supervision)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ResumenSesionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ResumenSesionDto>> Cerrar(Guid sesionId, CierreRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new CerrarCajaCommand(sesionId, request.MontoCierreReal, request.Observaciones), ct));

    public sealed record AperturaRequest(decimal MontoApertura);
    public sealed record CierreRequest(decimal MontoCierreReal, string? Observaciones);
}
