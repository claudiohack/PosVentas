using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Suscripciones;

namespace PosVentas.WebAPI.Controllers;

/// <summary>Suscripción del comercio autenticado: estado, plan, uso de los límites, planes disponibles y pagos.</summary>
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
}
