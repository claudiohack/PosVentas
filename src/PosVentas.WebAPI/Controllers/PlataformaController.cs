using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Suscripciones;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Administración comercial de la plataforma (HS Tech): planes, comercios y sus suscripciones.
/// Además de la política, cada caso de uso es <c>IRequiereAdministradorPlataforma</c>.
/// </summary>
[ApiController]
[Route("api/plataforma")]
[Authorize(Policy = Politicas.AdministradorGlobal)]
public sealed class PlataformaController(ISender mediator) : ControllerBase
{
    // ---------- Planes ----------

    [HttpGet("modulos")]
    [ProducesResponseType<IReadOnlyList<ModuloPlanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ModuloPlanDto>>> Modulos(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerModulosPlanQuery(), ct));

    [HttpGet("planes")]
    [ProducesResponseType<IReadOnlyList<PlanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> Planes(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerPlanesQuery(), ct));

    [HttpPost("planes")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlanDto>> CrearPlan(CrearPlanCommand command, CancellationToken ct)
    {
        var plan = await mediator.Send(command, ct);
        return Created($"/api/plataforma/planes/{plan.Id}", plan);
    }

    /// <summary>Precio, límites, módulos, visibilidad y si es el plan de la prueba gratuita. El código no se edita.</summary>
    [HttpPut("planes/{id:guid}")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlanDto>> ActualizarPlan(Guid id, ActualizarPlanCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command with { Id = id }, ct));

    // ---------- Comercios ----------

    [HttpGet("comercios")]
    [ProducesResponseType<IReadOnlyList<ComercioSuscripcionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ComercioSuscripcionDto>>> Comercios(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerComerciosSuscripcionQuery(), ct));

    [HttpGet("comercios/{id:guid}/pagos")]
    [ProducesResponseType<IReadOnlyList<PagoSuscripcionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PagoSuscripcionDto>>> Pagos(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerPagosComercioQuery(id), ct));

    public sealed record CambiarPlanRequest(Guid PlanId);

    [HttpPut("comercios/{id:guid}/plan")]
    [ProducesResponseType<ComercioSuscripcionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComercioSuscripcionDto>> CambiarPlan(Guid id, CambiarPlanRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new CambiarPlanComercioCommand(id, request.PlanId), ct));

    public sealed record PagoManualRequest(int Meses, decimal? Monto, string? Nota);

    /// <summary>Acredita meses de servicio cobrados por fuera (transferencia, efectivo, cortesía).</summary>
    [HttpPost("comercios/{id:guid}/pagos")]
    [ProducesResponseType<ComercioSuscripcionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComercioSuscripcionDto>> RegistrarPago(Guid id, PagoManualRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new RegistrarPagoManualCommand(id, request.Meses, request.Monto, request.Nota), ct));
}
