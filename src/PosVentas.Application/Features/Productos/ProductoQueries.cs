using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Application.Features.Productos;

// ---------- Listado ----------

public sealed record ObtenerProductosQuery(string? Buscar, bool IncluirInactivos = false)
    : IRequest<IReadOnlyList<ProductoDto>>;

public sealed class ObtenerProductosQueryValidator : AbstractValidator<ObtenerProductosQuery>
{
    public ObtenerProductosQueryValidator()
    {
        RuleFor(x => x.Buscar).MaximumLength(Producto.LongitudMaximaNombre);
    }
}

public sealed class ObtenerProductosQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerProductosQuery, IReadOnlyList<ProductoDto>>
{
    private const int LimiteResultados = 200;

    public async Task<IReadOnlyList<ProductoDto>> Handle(ObtenerProductosQuery request, CancellationToken cancellationToken)
    {
        // El filtro global de EF Core ya restringe al comercio actual.
        var query = db.Productos.AsNoTracking();

        if (!request.IncluirInactivos)
            query = query.Where(p => p.EstaActivo);

        if (!string.IsNullOrWhiteSpace(request.Buscar))
        {
            // Nombre parcial, o SKU / código de barras exactos (lo que manda un lector de códigos).
            var termino = request.Buscar.Trim();
            var codigo = termino.ToUpperInvariant();
            Sku? sku = null;
            try { sku = Sku.Crear(termino); } catch (Domain.Common.DomainException) { /* no es un SKU válido */ }
            query = sku is null
                ? query.Where(p => p.Nombre.Contains(termino) || p.CodigoBarras == codigo)
                : query.Where(p => p.Nombre.Contains(termino) || p.CodigoBarras == codigo || p.Sku == sku);
        }

        var productos = await query
            .OrderBy(p => p.Nombre)
            .Take(LimiteResultados)
            .Include(p => p.Categoria)
            .ToListAsync(cancellationToken);

        return productos.Select(ProductoMapper.ToDto).ToList();
    }
}

// ---------- Detalle ----------

public sealed record ObtenerProductoPorIdQuery(Guid Id) : IRequest<ProductoDto>;

public sealed class ObtenerProductoPorIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerProductoPorIdQuery, ProductoDto>
{
    public async Task<ProductoDto> Handle(ObtenerProductoPorIdQuery request, CancellationToken cancellationToken)
    {
        var producto = await db.Productos
            .AsNoTracking()
            .Include(p => p.Categoria)
            .SingleOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Producto), request.Id);

        return ProductoMapper.ToDto(producto);
    }
}

internal static class ProductoMapper
{
    public static ProductoDto ToDto(Producto p) => new(
        p.Id,
        p.Sku.Value,
        p.Nombre,
        p.CategoriaId,
        p.Categoria?.Nombre,
        p.PrecioCompra.Monto,
        p.PorcentajeGanancia,
        p.PrecioVenta.Monto,
        p.PrecioVenta.Moneda,
        p.PorcentajeIva,
        p.StockActual,
        p.StockMinimo,
        p.TieneStockBajo,
        p.VenderSinStock,
        p.EstaActivo,
        p.CodigoBarras);
}
