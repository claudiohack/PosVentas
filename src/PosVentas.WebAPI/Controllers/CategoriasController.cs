using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Categorias;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/categorias")]
public sealed class CategoriasController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [AutorizarPermiso(Permisos.ProductosConsultar)]
    [ProducesResponseType<IReadOnlyList<CategoriaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoriaDto>>> Listar([FromQuery] bool incluirInactivas, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCategoriasQuery(incluirInactivas), ct));

    [HttpPost]
    [AutorizarPermiso(Permisos.CategoriasGestionar)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(CrearCategoriaCommand command, CancellationToken ct)
    {
        var id = await mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [HttpPut("{id:guid}")]
    [AutorizarPermiso(Permisos.CategoriasGestionar)]
    [ProducesResponseType<CategoriaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoriaDto>> Actualizar(Guid id, DatosCategoriaRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarCategoriaCommand(id, r.Nombre, r.Descripcion, r.OrdenDisplay), ct));

    /// <summary>Activa o da de baja (lógica) la categoría. Con productos activos no se puede dar de baja (422).</summary>
    [HttpPatch("{id:guid}/estado")]
    [AutorizarPermiso(Permisos.CategoriasGestionar)]
    [ProducesResponseType<CategoriaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CategoriaDto>> CambiarEstado(Guid id, EstadoRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new CambiarEstadoCategoriaCommand(id, r.Activa), ct));

    /// <summary>Reordena las categorías: el orden de la lista de ids es el orden de visualización.</summary>
    [HttpPut("orden")]
    [AutorizarPermiso(Permisos.CategoriasGestionar)]
    [ProducesResponseType<IReadOnlyList<CategoriaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CategoriaDto>>> Reordenar(ReordenarCategoriasCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    public sealed record DatosCategoriaRequest(string Nombre, string? Descripcion, int OrdenDisplay);
    public sealed record EstadoRequest(bool Activa);
}
