using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Auditoria;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>Registro de auditoría del comercio (solo lectura: los registros no se editan ni se borran).</summary>
[ApiController]
[Route("api/auditoria")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AuditoriaController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [AutorizarPermiso(Permisos.AuditoriaConsultar)]
    [ProducesResponseType<PaginaAuditoriaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaAuditoriaDto>> Listar(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] string? entidad, [FromQuery] Guid? entidadId,
        CancellationToken ct, [FromQuery] int pagina = 1, [FromQuery] int tamanioPagina = 50)
        => Ok(await mediator.Send(new ObtenerAuditoriaQuery(desde, hasta, entidad, entidadId, pagina, tamanioPagina), ct));
}
