using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Proveedores;

namespace PosVentas.WebAPI.Controllers;

/// <summary>ABM de proveedores. Administradores y supervisores; un proveedor de otro comercio responde 404.</summary>
[ApiController]
[Route("api/proveedores")]
[Authorize(Policy = Politicas.Supervision)]
public sealed class ProveedoresController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProveedorDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProveedorDto>>> Listar(
        [FromQuery] string? buscar, [FromQuery] bool incluirInactivos, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProveedoresQuery(buscar, incluirInactivos), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProveedorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProveedorDto>> Obtener(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProveedorPorIdQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<ProveedorDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProveedorDto>> Crear(CrearProveedorCommand command, CancellationToken ct)
    {
        var proveedor = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Obtener), new { id = proveedor.Id }, proveedor);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProveedorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProveedorDto>> Actualizar(Guid id, DatosProveedorRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarProveedorCommand(id, r.RazonSocial, r.Cuit, r.Telefono, r.Email, r.Direccion), ct));

    /// <summary>Activa o da de baja (lógica) al proveedor. Uno dado de baja no admite compras nuevas.</summary>
    [HttpPatch("{id:guid}/estado")]
    [ProducesResponseType<ProveedorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProveedorDto>> CambiarEstado(Guid id, EstadoRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new CambiarEstadoProveedorCommand(id, r.Activo), ct));

    public sealed record DatosProveedorRequest(string RazonSocial, string Cuit, string? Telefono, string? Email, string? Direccion);
    public sealed record EstadoRequest(bool Activo);
}
