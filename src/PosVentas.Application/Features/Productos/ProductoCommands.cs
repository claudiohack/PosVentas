using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Application.Features.Productos;

// ---------- Alta ----------

public sealed record CrearProductoCommand(
    string Sku,
    string Nombre,
    decimal PrecioCompra,
    string? Moneda,
    decimal PorcentajeGanancia,
    decimal PorcentajeIva,
    Guid CategoriaId,
    bool VenderSinStock,
    decimal StockMinimo = 0) : IRequest<Guid>;

public sealed class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    private const decimal ImporteMaximo = 9_999_999_999_999_999.99m; // decimal(18,2)

    public CrearProductoCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Sku.LongitudMaxima);
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(Producto.LongitudMaximaNombre);
        RuleFor(x => x.PrecioCompra).InclusiveBetween(0, ImporteMaximo);
        RuleFor(x => x.Moneda).Length(3).When(x => !string.IsNullOrWhiteSpace(x.Moneda));
        RuleFor(x => x.PorcentajeGanancia).InclusiveBetween(0, 10_000);
        RuleFor(x => x.PorcentajeIva).InclusiveBetween(0, 100);
        RuleFor(x => x.CategoriaId).NotEmpty();
        RuleFor(x => x.StockMinimo).InclusiveBetween(0, 1_000_000);
    }
}

public sealed class CrearProductoCommandHandler(IApplicationDbContext db) : IRequestHandler<CrearProductoCommand, Guid>
{
    public async Task<Guid> Handle(CrearProductoCommand request, CancellationToken cancellationToken)
    {
        // Una categoría de otro comercio no es visible por el filtro global: responde 404.
        var categoriaExiste = await db.Categorias
            .AnyAsync(c => c.Id == request.CategoriaId && c.EstaActivo, cancellationToken);
        if (!categoriaExiste)
            throw new NotFoundException(nameof(Categoria), request.CategoriaId);

        var sku = Sku.Crear(request.Sku);
        if (await db.Productos.AnyAsync(p => p.Sku == sku, cancellationToken))
            throw new ConflictException($"Ya existe un producto con el SKU '{sku}'.");

        var producto = Producto.Crear(
            sku,
            request.Nombre,
            Money.Crear(request.PrecioCompra, request.Moneda),
            request.PorcentajeGanancia,
            request.PorcentajeIva,
            request.CategoriaId,
            request.VenderSinStock,
            request.StockMinimo);

        db.Productos.Add(producto);
        await db.SaveChangesAsync(cancellationToken);
        return producto.Id;
    }
}

// ---------- Ingreso de stock ----------

public sealed record IngresarStockCommand(Guid ProductoId, decimal Cantidad) : IRequest<decimal>;

public sealed class IngresarStockCommandValidator : AbstractValidator<IngresarStockCommand>
{
    public IngresarStockCommandValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty();
        RuleFor(x => x.Cantidad).GreaterThan(0).LessThanOrEqualTo(1_000_000);
    }
}

public sealed class IngresarStockCommandHandler(IApplicationDbContext db) : IRequestHandler<IngresarStockCommand, decimal>
{
    public async Task<decimal> Handle(IngresarStockCommand request, CancellationToken cancellationToken)
    {
        var producto = await db.Productos.SingleOrDefaultAsync(p => p.Id == request.ProductoId, cancellationToken)
            ?? throw new NotFoundException(nameof(Producto), request.ProductoId);

        producto.IngresarStock(request.Cantidad);
        await db.SaveChangesAsync(cancellationToken);
        return producto.StockActual;
    }
}
