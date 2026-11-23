using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Productos;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Catálogo de productos del comercio autenticado. El aislamiento por comercio lo garantiza
/// el filtro global de EF Core: un producto de otro comercio responde 404.
/// </summary>
[ApiController]
[Route("api/productos")]
public sealed class ProductosController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [AutorizarPermiso(Permisos.ProductosConsultar)]
    [ProducesResponseType<IReadOnlyList<ProductoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductoDto>>> Listar(
        [FromQuery] string? buscar,
        [FromQuery] bool incluirInactivos,
        CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductosQuery(buscar, incluirInactivos), ct));

    /// <summary>Productos activos con existencias menores o iguales a su stock mínimo.</summary>
    [HttpGet("stock-bajo")]
    [AutorizarPermiso(Permisos.ProductosConsultar)]
    [ProducesResponseType<IReadOnlyList<ProductoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductoDto>>> StockBajo(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductosStockBajoQuery(), ct));

    /// <summary>Fija el umbral de alerta de stock bajo.</summary>
    [HttpPut("{id:guid}/stock-minimo")]
    [AutorizarPermiso(Permisos.ProductosStockMinimo)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> ConfigurarStockMinimo(Guid id, StockMinimoRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new ConfigurarStockMinimoCommand(id, request.StockMinimo), ct));

    [HttpGet("{id:guid}")]
    [AutorizarPermiso(Permisos.ProductosConsultar)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> ObtenerPorId(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerProductoPorIdQuery(id), ct));

    [HttpPost]
    [AutorizarPermiso(Permisos.ProductosGestionar)]
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
    [AutorizarPermiso(Permisos.ProductosGestionar)]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockResponse>> IngresarStock(Guid id, IngresarStockRequest request, CancellationToken ct)
    {
        var stock = await mediator.Send(new IngresarStockCommand(id, request.Cantidad), ct);
        return Ok(new StockResponse(id, stock));
    }

    /// <summary>Edita datos, código de barras, categoría, costo y precio (por margen o precio de venta fijo).</summary>
    [HttpPut("{id:guid}")]
    [AutorizarPermiso(Permisos.ProductosGestionar)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductoDto>> Actualizar(Guid id, ActualizarProductoRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarProductoCommand(id, r.Sku, r.Nombre, r.CodigoBarras, r.CategoriaId, r.PrecioCompra,
            r.PorcentajeGanancia, r.PrecioVenta, r.PorcentajeIva, r.VenderSinStock, r.StockMinimo), ct));

    /// <summary>Activa o da de baja (lógica) el producto. Uno dado de baja no se vende ni se compra.</summary>
    [HttpPatch("{id:guid}/estado")]
    [AutorizarPermiso(Permisos.ProductosGestionar)]
    [ProducesResponseType<ProductoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDto>> CambiarEstado(Guid id, EstadoProductoRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new CambiarEstadoProductoCommand(id, r.Activo), ct));

    /// <summary>Descarga el catálogo completo (incluye dados de baja) en Excel. Sirve también de plantilla para importar.</summary>
    [HttpGet("exportar")]
    [AutorizarPermiso(Permisos.ProductosExportar)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Exportar(CancellationToken ct)
    {
        var archivo = await mediator.Send(new ExportarProductosQuery(), ct);
        return File(archivo.Contenido, archivo.TipoContenido, $"productos-{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Alta y actualización masiva desde Excel, por SKU. Con <c>simular=true</c> devuelve la vista previa sin guardar.
    /// Es todo o nada: si alguna fila tiene errores no se guarda ninguna (<c>aplicado=false</c> y la lista de errores).
    /// </summary>
    [HttpPost("importar")]
    [AutorizarPermiso(Permisos.ProductosImportar)]
    [RequestSizeLimit(ImportarProductosCommandValidator.TamanioMaximo + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportarProductosCommandValidator.TamanioMaximo + 64 * 1024)]
    [ProducesResponseType<ResultadoImportacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultadoImportacionDto>> Importar(IFormFile? archivo, [FromQuery] bool simular, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Planilla inválida.", detail: "Elegí una planilla .xlsx.");
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Planilla inválida.",
                detail: "Solo se aceptan planillas .xlsx (Excel 2007 o posterior). Si tenés un .xls o .csv, abrilo en Excel y guardalo como .xlsx.");
        if (archivo.Length > ImportarProductosCommandValidator.TamanioMaximo)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Planilla inválida.", detail: "La planilla no puede superar los 5 MB.");

        using var ms = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(ms, ct);
        return Ok(await mediator.Send(new ImportarProductosCommand(ms.ToArray(), simular), ct));
    }

    public sealed record ActualizarProductoRequest(
        string Sku, string Nombre, string? CodigoBarras, Guid CategoriaId, decimal PrecioCompra, decimal PorcentajeGanancia,
        decimal? PrecioVenta, decimal PorcentajeIva, bool VenderSinStock, decimal StockMinimo);
    public sealed record EstadoProductoRequest(bool Activo);
    public sealed record IngresarStockRequest(decimal Cantidad);
    public sealed record StockMinimoRequest(decimal StockMinimo);
    public sealed record StockResponse(Guid ProductoId, decimal StockActual);
}
