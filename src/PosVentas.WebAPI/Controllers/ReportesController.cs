using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Reportes;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Reportes financieros del comercio autenticado. Administradores y supervisores (política Supervision).
/// Fechas en formato yyyy-MM-dd, calendario argentino, ambos extremos incluidos; sin fechas = hoy.
/// </summary>
[ApiController]
[Route("api/reportes")]
[Authorize(Policy = Politicas.Supervision)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ReportesController(ISender mediator) : ControllerBase
{
    /// <summary>Total vendido, operaciones, ticket promedio, margen bruto estimado e ingresos por medio de pago.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardResumenDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardResumenDto>> Dashboard([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Ok(await mediator.Send(new GetDashboardResumenQuery(desde, hasta), ct));

    /// <summary>Ranking de productos por unidades o por recaudación. Opcionalmente filtrado por categoría.</summary>
    [HttpGet("top-productos")]
    [ProducesResponseType<TopProductosDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TopProductosDto>> TopProductos(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] int top = 10,
        [FromQuery] OrdenRanking ordenarPor = OrdenRanking.Unidades, [FromQuery] Guid? categoriaId = null, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetTopProductosVendidosQuery(desde, hasta, top, ordenarPor, categoriaId), ct));

    /// <summary>Venta, costo, margen y participación de cada categoría.</summary>
    [HttpGet("categorias")]
    [ProducesResponseType<RentabilidadPorCategoriaDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RentabilidadPorCategoriaDto>> Categorias([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Ok(await mediator.Send(new GetRentabilidadPorCategoriaQuery(desde, hasta), ct));

    /// <summary>Turnos cerrados en el período con su arqueo, y sobrantes / faltantes acumulados.</summary>
    [HttpGet("cajas")]
    [ProducesResponseType<ReporteCierresCajaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReporteCierresCajaDto>> Cajas(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] Guid? cajaId, CancellationToken ct)
        => Ok(await mediator.Send(new GetReporteCierresCajaQuery(desde, hasta, cajaId), ct));
}
