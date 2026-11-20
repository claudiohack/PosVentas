using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Productos;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Catálogo de productos del comercio autenticado. El aislamiento por comercio lo garantiza
/// el filtro global de EF Core: un producto de otro comercio responde 404.
/// </summary>
[ApiController]
[Route("api/productos")]
[Authorize(Policy = Politicas.Operador)]
public sealed class ProductosController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProductoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductoDto>>> Listar(
        [FromQuery] string? buscar,
        [FromQuery] bool incluirInactivos,
        CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductosQuery(buscar, incluirInactivos), ct));

    /// <summary>Productos activos con existencias menores o iguales a su stock mínimo.</summary>
    [HttpGet("stock-bajo")]
    [ProducesResponseType<IReadOnlyList<ProductoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductoDto>>> StockBajo(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductosStockBajoQuery(), ct));

    /// <summary>Fija el umbral de alerta de stock bajo.</summary>
    [HttpPut("{id:guid}/stock-minimo")]
    [Authorize(Policy = Politicas.Supervision)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> ConfigurarStockMinimo(Guid id, StockMinimoRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new ConfigurarStockMinimoCommand(id, request.StockMinimo), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> ObtenerPorId(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductoPorIdQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Crear(CrearProductoCommand command, CancellationToken ct)
    {
        var id = await mediator.Send(command, ct);
        var producto = await mediator.Send(new ObtenerProductoPorIdQuery(id), ct);
        return CreatedAtAction(nameof(ObtenerPorId), new { id }, producto);
    }

    [HttpPost("{id:guid}/stock/ingreso")]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockResponse>> IngresarStock(Guid id, IngresarStockRequest request, CancellationToken ct)
    {
        var stock = await mediator.Send(new IngresarStockCommand(id, request.Cantidad), ct);
        return Ok(new StockResponse(id, stock));
    }

    public sealed record IngresarStockRequest(decimal Cantidad);
    public sealed record StockMinimoRequest(decimal StockMinimo);
    public sealed record StockResponse(Guid ProductoId, decimal StockActual);
}
