using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Productos;

// ---------- Alertas de stock bajo ----------

/// <summary>Productos activos con StockActual &lt;= StockMinimo, los más críticos primero.</summary>
public sealed record ObtenerProductosStockBajoQuery : IRequest<IReadOnlyList<ProductoDto>>;

public sealed class ObtenerProductosStockBajoQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerProductosStockBajoQuery, IReadOnlyList<ProductoDto>>
{
    private const int LimiteResultados = 500;

    public async Task<IReadOnlyList<ProductoDto>> Handle(ObtenerProductosStockBajoQuery request, CancellationToken ct)
    {
        var productos = await db.Productos.AsNoTracking()
            .Where(p => p.EstaActivo && p.StockActual <= p.StockMinimo)
            .OrderBy(p => p.StockActual - p.StockMinimo)
            .ThenBy(p => p.Nombre)
            .Take(LimiteResultados)
            .Include(p => p.Categoria)
            .ToListAsync(ct);

        return productos.Select(ProductoMapper.ToDto).ToList();
    }
}

// ---------- Configuración del umbral ----------

public sealed record ConfigurarStockMinimoCommand(Guid ProductoId, decimal StockMinimo) : IRequest<ProductoDto>;

public sealed class ConfigurarStockMinimoCommandValidator : AbstractValidator<ConfigurarStockMinimoCommand>
{
    public ConfigurarStockMinimoCommandValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty();
        RuleFor(x => x.StockMinimo).InclusiveBetween(0, 1_000_000).PrecisionScale(18, 3, true);
    }
}

public sealed class ConfigurarStockMinimoCommandHandler(IApplicationDbContext db)
    : IRequestHandler<ConfigurarStockMinimoCommand, ProductoDto>
{
    public async Task<ProductoDto> Handle(ConfigurarStockMinimoCommand request, CancellationToken ct)
    {
        var producto = await db.Productos.Include(p => p.Categoria)
            .SingleOrDefaultAsync(p => p.Id == request.ProductoId, ct)
            ?? throw new NotFoundException(nameof(Producto), request.ProductoId);

        producto.ConfigurarStockMinimo(request.StockMinimo);
        await db.SaveChangesAsync(ct);
        return ProductoMapper.ToDto(producto);
    }
}
