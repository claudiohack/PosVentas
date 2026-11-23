using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Auditoria;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Suscripciones;
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
    decimal StockMinimo = 0,
    string? CodigoBarras = null,
    decimal StockInicial = 0,
    decimal? PrecioVenta = null) : IRequest<Guid>, IDatosProducto;

/// <summary>Datos de precio y clasificación comunes al alta y a la edición.</summary>
public interface IDatosProducto
{
    string Sku { get; }
    string Nombre { get; }
    string? CodigoBarras { get; }
    decimal PrecioCompra { get; }
    decimal PorcentajeGanancia { get; }
    decimal? PrecioVenta { get; }
    decimal PorcentajeIva { get; }
    Guid CategoriaId { get; }
    decimal StockMinimo { get; }
}

public sealed class DatosProductoValidator : AbstractValidator<IDatosProducto>
{
    private const decimal ImporteMaximo = 9_999_999_999_999_999.99m; // decimal(18,2)

    public DatosProductoValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Sku.LongitudMaxima);
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(Producto.LongitudMaximaNombre);
        RuleFor(x => x.CodigoBarras).MaximumLength(Producto.LongitudMaximaCodigoBarras)
            .Matches("^[A-Za-z0-9]+$").When(x => !string.IsNullOrWhiteSpace(x.CodigoBarras))
            .WithMessage("El código de barras admite solo letras y números, sin espacios.");
        RuleFor(x => x.PrecioCompra).InclusiveBetween(0, ImporteMaximo);
        RuleFor(x => x.PorcentajeGanancia).InclusiveBetween(0, 10_000);
        RuleFor(x => x.PrecioVenta).GreaterThanOrEqualTo(x => x.PrecioCompra).When(x => x.PrecioVenta.HasValue)
            .WithMessage("El precio de venta no puede ser menor al costo.");
        RuleFor(x => x.PrecioVenta).LessThanOrEqualTo(ImporteMaximo).PrecisionScale(18, 2, true).When(x => x.PrecioVenta.HasValue);
        RuleFor(x => x.PrecioCompra).GreaterThan(0).When(x => x.PrecioVenta.HasValue)
            .WithMessage("Para fijar el precio de venta indicá un costo mayor a cero (o usá el margen).");
        RuleFor(x => x.PorcentajeIva).Must(iva => iva is 0m or 2.5m or 5m or 10.5m or 21m or 27m)
            .WithMessage("El IVA debe ser una alícuota vigente: 0, 2,5, 5, 10,5, 21 o 27 %.");
        RuleFor(x => x.CategoriaId).NotEmpty();
        RuleFor(x => x.StockMinimo).InclusiveBetween(0, 1_000_000);
    }
}

internal static class ReglasProducto
{
    public static async Task ExigirCategoriaActivaAsync(IApplicationDbContext db, Guid categoriaId, CancellationToken ct)
    {
        // Una categoría de otro comercio no es visible por el filtro global: responde 404.
        if (!await db.Categorias.AnyAsync(c => c.Id == categoriaId && c.EstaActivo, ct))
            throw new NotFoundException(nameof(Categoria), categoriaId);
    }

    public static async Task ExigirIdentificadoresLibresAsync(IApplicationDbContext db, Sku sku, string? codigoBarras, Guid? exceptoId, CancellationToken ct)
    {
        if (await db.Productos.AnyAsync(p => p.Sku == sku && p.Id != exceptoId, ct))
            throw new ConflictException($"Ya existe un producto con el SKU '{sku}'.");
        if (codigoBarras is not null && await db.Productos.AnyAsync(p => p.CodigoBarras == codigoBarras && p.Id != exceptoId, ct))
            throw new ConflictException($"Ya existe un producto con el código de barras '{codigoBarras}'.");
    }

    /// <summary>Precio por margen, o precio de venta fijo (el margen se recalcula).</summary>
    public static void AplicarPrecios(Producto producto, Money costo, decimal porcentajeGanancia, decimal? precioVenta)
    {
        if (precioVenta is { } pv)
            producto.FijarPrecioVenta(costo, Money.Crear(pv, costo.Moneda));
        else
            producto.AsignarPreciosYMargen(costo, porcentajeGanancia);
    }

    public static string DescribirPrecios(Producto p) =>
        $"Costo {RegistradorAuditoria.Importe(p.PrecioCompra.Monto)}, margen {RegistradorAuditoria.Cantidad(p.PorcentajeGanancia)} %, " +
        $"precio de venta {RegistradorAuditoria.Importe(p.PrecioVenta.Monto)}";
}

public sealed class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    public CrearProductoCommandValidator()
    {
        Include(new DatosProductoValidator());
        RuleFor(x => x.Moneda).Length(3).When(x => !string.IsNullOrWhiteSpace(x.Moneda));
        RuleFor(x => x.StockInicial).InclusiveBetween(0, 1_000_000).PrecisionScale(18, 3, true);
    }
}

public sealed class CrearProductoCommandHandler(IApplicationDbContext db, RegistradorAuditoria auditoria, SuscripcionActual suscripcion)
    : IRequestHandler<CrearProductoCommand, Guid>
{
    public async Task<Guid> Handle(CrearProductoCommand request, CancellationToken cancellationToken)
    {
        await ReglasProducto.ExigirCategoriaActivaAsync(db, request.CategoriaId, cancellationToken);
        await ReglasPlan.ExigirCupoProductosAsync(db, suscripcion, cancellationToken);

        var sku = Sku.Crear(request.Sku);
        var codigoBarras = Producto.NormalizarCodigoBarras(request.CodigoBarras);
        await ReglasProducto.ExigirIdentificadoresLibresAsync(db, sku, codigoBarras, null, cancellationToken);

        var costo = Money.Crear(request.PrecioCompra, request.Moneda);
        var producto = Producto.Crear(
            sku,
            request.Nombre,
            costo,
            request.PorcentajeGanancia,
            request.PorcentajeIva,
            request.CategoriaId,
            request.VenderSinStock,
            request.StockMinimo);
        producto.AsignarCodigoBarras(codigoBarras);
        if (request.PrecioVenta.HasValue)
            ReglasProducto.AplicarPrecios(producto, costo, request.PorcentajeGanancia, request.PrecioVenta);
        if (request.StockInicial > 0)
            producto.IngresarStock(request.StockInicial);

        db.Productos.Add(producto);
        auditoria.Registrar(AccionesAuditoria.ProductoCreado, nameof(Producto), producto.Id,
            $"Producto '{producto.Nombre}' (SKU {producto.Sku}). {ReglasProducto.DescribirPrecios(producto)}; " +
            $"stock inicial: {RegistradorAuditoria.Cantidad(producto.StockActual)}.");
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

public sealed class IngresarStockCommandHandler(IApplicationDbContext db, RegistradorAuditoria auditoria) : IRequestHandler<IngresarStockCommand, decimal>
{
    public async Task<decimal> Handle(IngresarStockCommand request, CancellationToken cancellationToken)
    {
        var producto = await db.Productos.SingleOrDefaultAsync(p => p.Id == request.ProductoId, cancellationToken)
            ?? throw new NotFoundException(nameof(Producto), request.ProductoId);

        var anterior = producto.StockActual;
        producto.IngresarStock(request.Cantidad);
        auditoria.Registrar(AccionesAuditoria.ProductoStockIngresado, nameof(Producto), producto.Id,
            $"Ingreso manual de {RegistradorAuditoria.Cantidad(request.Cantidad)} unidades de '{producto.Nombre}' " +
            $"(stock {RegistradorAuditoria.Cantidad(anterior)} → {RegistradorAuditoria.Cantidad(producto.StockActual)}).");
        await db.SaveChangesAsync(cancellationToken);
        return producto.StockActual;
    }
}

// ---------- Edición ----------

/// <param name="PrecioVenta">Si se indica, fija el precio exacto y recalcula el margen; si no, el precio sale del margen.</param>
public sealed record ActualizarProductoCommand(
    Guid Id,
    string Sku,
    string Nombre,
    string? CodigoBarras,
    Guid CategoriaId,
    decimal PrecioCompra,
    decimal PorcentajeGanancia,
    decimal? PrecioVenta,
    decimal PorcentajeIva,
    bool VenderSinStock,
    decimal StockMinimo) : IRequest<ProductoDto>, IDatosProducto;

public sealed class ActualizarProductoCommandValidator : AbstractValidator<ActualizarProductoCommand>
{
    public ActualizarProductoCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        Include(new DatosProductoValidator());
    }
}

public sealed class ActualizarProductoCommandHandler(IApplicationDbContext db, RegistradorAuditoria auditoria) : IRequestHandler<ActualizarProductoCommand, ProductoDto>
{
    public async Task<ProductoDto> Handle(ActualizarProductoCommand r, CancellationToken ct)
    {
        var producto = await db.Productos.SingleOrDefaultAsync(p => p.Id == r.Id, ct)
            ?? throw new NotFoundException(nameof(Producto), r.Id);

        if (r.CategoriaId != producto.CategoriaId)
            await ReglasProducto.ExigirCategoriaActivaAsync(db, r.CategoriaId, ct);

        var sku = Sku.Crear(r.Sku);
        var codigoBarras = Producto.NormalizarCodigoBarras(r.CodigoBarras);
        await ReglasProducto.ExigirIdentificadoresLibresAsync(db, sku, codigoBarras, producto.Id, ct);

        var preciosAnteriores = ReglasProducto.DescribirPrecios(producto);
        var (costoAnterior, ventaAnterior) = (producto.PrecioCompra.Monto, producto.PrecioVenta.Monto);

        producto.ActualizarDatosBasicos(sku, r.Nombre, r.PorcentajeIva);
        producto.AsignarCodigoBarras(codigoBarras);
        producto.CambiarCategoria(r.CategoriaId);
        ReglasProducto.AplicarPrecios(producto, Money.Crear(r.PrecioCompra, producto.PrecioCompra.Moneda), r.PorcentajeGanancia, r.PrecioVenta);
        producto.ConfigurarVentaSinStock(r.VenderSinStock);
        producto.ConfigurarStockMinimo(r.StockMinimo);

        // Solo se audita lo que afecta la plata: costo o precio de venta (el margen se deriva de ambos).
        if (producto.PrecioCompra.Monto != costoAnterior || producto.PrecioVenta.Monto != ventaAnterior)
            auditoria.Registrar(AccionesAuditoria.ProductoPrecioModificado, nameof(Producto), producto.Id,
                $"'{producto.Nombre}' (SKU {producto.Sku}). Antes: {preciosAnteriores}. Ahora: {ReglasProducto.DescribirPrecios(producto)}.");

        await db.SaveChangesAsync(ct); // RowVersion: si una venta tocó el producto en el medio, 409
        await db.Categorias.Where(c => c.Id == producto.CategoriaId).LoadAsync(ct);
        return ProductoMapper.ToDto(producto);
    }
}

// ---------- Baja lógica ----------

public sealed record CambiarEstadoProductoCommand(Guid Id, bool Activo) : IRequest<ProductoDto>;

public sealed class CambiarEstadoProductoCommandHandler(IApplicationDbContext db, RegistradorAuditoria auditoria, SuscripcionActual suscripcion)
    : IRequestHandler<CambiarEstadoProductoCommand, ProductoDto>
{
    public async Task<ProductoDto> Handle(CambiarEstadoProductoCommand request, CancellationToken ct)
    {
        var producto = await db.Productos.Include(p => p.Categoria).SingleOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Producto), request.Id);

        var cambia = producto.EstaActivo != request.Activo;
        if (cambia && request.Activo)
            await ReglasPlan.ExigirCupoProductosAsync(db, suscripcion, ct);
        if (request.Activo) producto.Activar(); else producto.Desactivar();
        if (cambia)
            auditoria.Registrar(request.Activo ? AccionesAuditoria.ProductoActivado : AccionesAuditoria.ProductoDesactivado,
                nameof(Producto), producto.Id,
                $"Producto '{producto.Nombre}' (SKU {producto.Sku}) {(request.Activo ? "reactivado" : "dado de baja")}.");
        await db.SaveChangesAsync(ct);
        return ProductoMapper.ToDto(producto);
    }
}
