using FluentValidation;
using MediatR;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Repositories;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Application.Features.Productos.Commands;

// Actualizamos el Record para recibir los nuevos datos financieros necesarios
public record CrearProductoCommand(
    string SkuCode,
    string Nombre,
    decimal PrecioCompraMonto,   // <-- Modificado: Ahora ingresa el Costo
    decimal PorcentajeGanancia,  // <-- Nuevo: Margen de utilidad
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
        RuleFor(x => x.PrecioCompraMonto).GreaterThanOrEqualTo(0).WithMessage("El precio de compra no puede ser negativo.");
        RuleFor(x => x.PorcentajeGanancia).GreaterThanOrEqualTo(0).WithMessage("El porcentaje de ganancia no puede ser negativo.");
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

        // 1. Instanciamos el objeto de valor para el costo usando la moneda correspondiente
        var precioCompra = new Money(request.PrecioCompraMonto, request.Moneda);

        // 2. Invocamos el constructor actualizado que calcula el precio de venta automáticamente
        var nuevoProducto = new Producto(
            id: Guid.NewGuid(),
            sku: skuObjeto,
            nombre: request.Nombre,
            precioCompra: precioCompra,
            porcentajeGanancia: request.PorcentajeGanancia,
            porcentajeIva: request.PorcentajeIva,
            categoriaId: request.CategoriaId,
            venderSinStock: request.VenderSinStock
        );

        await productoRepository.AddAsync(nuevoProducto, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return nuevoProducto.Id;
    }
}