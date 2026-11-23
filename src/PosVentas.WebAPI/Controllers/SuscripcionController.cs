using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Suscripciones;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>Suscripción del comercio autenticado: estado, plan, uso de los límites, planes disponibles, pagos y débito automático.</summary>
[ApiController]
[Route("api/suscripcion")]
public sealed class SuscripcionController(ISender mediator) : ControllerBase
{
    /// <summary>Cualquier usuario del comercio la ve (política de respaldo): así todos entienden un aviso de vencimiento.</summary>
    [HttpGet]
    [ProducesResponseType<MiSuscripcionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MiSuscripcionDto>> Obtener(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerMiSuscripcionQuery(), ct));

    public sealed record PagoMercadoPagoRequest(Guid PlanId, string EmailPagador);

    /// <summary>
    /// Inicia el débito automático con Mercado Pago para el plan elegido y devuelve el enlace donde autorizarlo.
    /// Solo el Administrador. Funciona aunque la suscripción esté vencida: es la forma de regularizarla.
    /// </summary>
    [HttpPost("mercadopago")]
    [PermitidoConSuscripcionVencida]
    [ProducesResponseType<InicioPagoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InicioPagoDto>> IniciarPago(PagoMercadoPagoRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new IniciarPagoMercadoPagoCommand(request.PlanId, request.EmailPagador), ct));

    /// <summary>Cancela el débito automático: no hay más cobros y el servicio sigue hasta el vencimiento ya pagado.</summary>
    [HttpDelete("mercadopago")]
    [PermitidoConSuscripcionVencida]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelarDebito(CancellationToken ct)
    {
        await mediator.Send(new CancelarDebitoAutomaticoCommand(), ct);
        return NoContent();
    }
}
