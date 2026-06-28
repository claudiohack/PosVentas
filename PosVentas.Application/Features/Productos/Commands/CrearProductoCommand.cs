using FluentValidation;
using MediatR;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Repositories;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Application.Features.Productos.Commands;

public record CrearProductoCommand(
    string SkuCode,
    string Nombre,
    decimal PrecioMonto,
    string Moneda,
    decimal PorcentajeIva,
    Guid CategoriaId,
    bool VenderSinStock) : IRequest<Guid>;

public class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    public CrearProductoCommandValidator()
    {
        RuleFor(x => x.SkuCode).NotEmpty().WithMessage("El código SKU es obligatorio.");
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx 150 cats).");
        RuleFor(x => x.PrecioMonto).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.PorcentajeIva).InclusiveBetween(0, 100).WithMessage("El IVA debe estar entre 0 y 100.");
        RuleFor(x => x.CategoriaId).NotEmpty().WithMessage("Debe asociar una categoría válida.");
    }
}

public class CrearProductoCommandHandler(
    IProductoRepository productoRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CrearProductoCommand, Guid>
{
    public async Task<Guid> Handle(CrearProductoCommand request, CancellationToken cancellationToken)
    {
        var skuObjeto = new Sku(request.SkuCode);
        var productoExistente = await productoRepository.GetBySkuAsync(skuObjeto, cancellationToken);
        if (productoExistente is not null)
            throw new InvalidOperationException($"Ya existe un producto con el SKU '{request.SkuCode}'.");

        var precioVenta = new Money(request.PrecioMonto, request.Moneda);
        var nuevoProducto = new Producto(
            Guid.NewGuid(),
            skuObjeto,
            request.Nombre,
            precioVenta,
            request.PorcentajeIva,
            request.CategoriaId,
            request.VenderSinStock
        );

        await productoRepository.AddAsync(nuevoProducto, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return nuevoProducto.Id;
    }
}