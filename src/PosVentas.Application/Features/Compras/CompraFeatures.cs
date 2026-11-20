using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Cajas;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Compras;

public sealed record CompraDetalleDto(Guid ProductoId, string Producto, decimal Cantidad, decimal CostoUnitario, decimal Subtotal);

public sealed record CompraDto(
    Guid Id,
    Guid ProveedorId,
    string Proveedor,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    decimal MontoTotal,
    Guid? CajaSesionId,
    DateTime FechaRegistroUtc,
    string? Observaciones,
    IReadOnlyList<CompraDetalleDto> Detalles);

public sealed record CompraResumenDto(
    Guid Id,
    Guid ProveedorId,
    string Proveedor,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    decimal MontoTotal,
    int CantidadItems,
    bool ImpactoEnCaja);

// ---------- Registro (atómico: compra + stock + costo + egreso de caja) ----------

public sealed record ItemCompraRequest(Guid ProductoId, decimal Cantidad, decimal CostoUnitario);

/// <param name="CajaSesionId">
/// Turno del que sale el pago al contado. Si se omite y hay una sola caja abierta, se usa esa;
/// si no hay ninguna abierta, la compra se registra sin impacto en caja.
/// </param>
public sealed record RegistrarCompraCommand(
    Guid ProveedorId,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    Guid? CajaSesionId,
    string? Observaciones,
    IReadOnlyList<ItemCompraRequest> Items) : IRequest<CompraDto>;

public sealed class RegistrarCompraCommandValidator : AbstractValidator<RegistrarCompraCommand>
{
    public RegistrarCompraCommandValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.NumeroComprobante).NotEmpty().MaximumLength(Compra.LongitudMaximaComprobante);
        RuleFor(x => x.FechaCompra).NotEmpty();
        RuleFor(x => x.FormaPago).IsInEnum();
        RuleFor(x => x.CajaSesionId).Null()
            .When(x => x.FormaPago == FormaPagoCompra.CuentaCorrienteProveedor)
            .WithMessage("Una compra a cuenta corriente no sale de la caja: no indiques turno de caja.");
        RuleFor(x => x.Observaciones).MaximumLength(Compra.LongitudMaximaObservaciones);

        RuleFor(x => x.Items).NotEmpty().WithMessage("La compra debe tener al menos un producto.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(300).When(x => x.Items is not null);
        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.ProductoId).Distinct().Count() == items.Count)
            .When(x => x.Items is { Count: > 0 })
            .WithMessage("Hay productos repetidos: unificá las cantidades en un solo renglón.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductoId).NotEmpty();
            item.RuleFor(i => i.Cantidad).GreaterThan(0).LessThanOrEqualTo(1_000_000).PrecisionScale(18, 3, true);
            item.RuleFor(i => i.CostoUnitario).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1_000_000_000m).PrecisionScale(18, 2, true);
        });
    }
}

public sealed class RegistrarCompraCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<RegistrarCompraCommand, CompraDto>
{
    public async Task<CompraDto> Handle(RegistrarCompraCommand request, CancellationToken ct)
    {
        var usuarioId = await UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, ct);

        // Proveedor y productos de otro comercio no son visibles por el filtro global: 404.
        var proveedor = await db.Proveedores.SingleOrDefaultAsync(p => p.Id == request.ProveedorId, ct)
            ?? throw new NotFoundException("el proveedor", request.ProveedorId);

        var numero = Compra.NormalizarComprobante(request.NumeroComprobante);
        if (await db.Compras.AnyAsync(c => c.ProveedorId == proveedor.Id && c.NumeroComprobante == numero, ct))
            throw new ConflictException($"El comprobante {numero} de {proveedor.RazonSocial} ya fue cargado.");

        var ids = request.Items.Select(i => i.ProductoId).ToList();
        var productos = await db.Productos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var faltante = ids.FirstOrDefault(id => !productos.ContainsKey(id));
        if (faltante != Guid.Empty)
            throw new NotFoundException(nameof(Producto), faltante);

        var compra = Compra.Iniciar(proveedor, numero, request.FechaCompra, request.FormaPago, usuarioId, request.Observaciones);
        foreach (var item in request.Items)
            compra.AgregarItem(productos[item.ProductoId], item.Cantidad, item.CostoUnitario);

        var sesion = await ResolverSesionDePagoAsync(request, ct);
        if (sesion is not null && compra.FormaPago == FormaPagoCompra.Efectivo)
            await ExigirEfectivoSuficienteAsync(sesion, compra.MontoTotal, ct);

        var egreso = compra.Confirmar(sesion);

        db.Compras.Add(compra);
        if (egreso is not null)
            db.MovimientosCaja.Add(egreso);

        // Una sola transacción: compra, detalles, stock y costo de cada producto, egreso y sesión de caja.
        // Las RowVersion de Producto y CajaSesion convierten una venta o un cierre simultáneos en 409.
        await db.SaveChangesAsync(ct);

        return CompraMapper.ToDto(compra, proveedor.RazonSocial);
    }

    private async Task<CajaSesion?> ResolverSesionDePagoAsync(RegistrarCompraCommand request, CancellationToken ct)
    {
        if (request.FormaPago == FormaPagoCompra.CuentaCorrienteProveedor)
            return null;

        if (request.CajaSesionId is { } sesionId)
        {
            var indicada = await db.CajaSesiones.SingleOrDefaultAsync(s => s.Id == sesionId, ct)
                ?? throw new NotFoundException("Sesión de caja", sesionId);
            if (!indicada.EnCurso)
                throw new DomainException("La caja indicada está cerrada: no se puede pagar desde ella.");
            return indicada;
        }

        var abiertas = await db.CajaSesiones.Where(s => s.EnCurso).Take(2).ToListAsync(ct);
        return abiertas.Count switch
        {
            0 => null,
            1 => abiertas[0],
            _ => throw new DomainException("Hay más de una caja abierta: indicá desde cuál se paga la compra.")
        };
    }

    private async Task ExigirEfectivoSuficienteAsync(CajaSesion sesion, decimal monto, CancellationToken ct)
    {
        var disponible = sesion.MontoApertura + await ResumenSesionBuilder.TotalEfectivoAsync(db, sesion.Id, ct);
        if (monto > disponible)
            throw new DomainException(
                $"El efectivo en caja ({disponible:0.00}) no alcanza para pagar la compra ({monto:0.00}).");
    }
}

// ---------- Consultas ----------

public sealed record ObtenerComprasQuery(Guid? ProveedorId, DateOnly? Desde, DateOnly? Hasta) : IRequest<IReadOnlyList<CompraResumenDto>>;

public sealed class ObtenerComprasQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerComprasQuery, IReadOnlyList<CompraResumenDto>>
{
    private const int LimiteResultados = 200;

    public async Task<IReadOnlyList<CompraResumenDto>> Handle(ObtenerComprasQuery request, CancellationToken ct)
    {
        var query = db.Compras.AsNoTracking();
        if (request.ProveedorId is { } proveedorId) query = query.Where(c => c.ProveedorId == proveedorId);
        if (request.Desde is { } desde) query = query.Where(c => c.FechaCompra >= desde);
        if (request.Hasta is { } hasta) query = query.Where(c => c.FechaCompra <= hasta);

        return await query
            .OrderByDescending(c => c.FechaCompra).ThenByDescending(c => c.FechaRegistroUtc)
            .Take(LimiteResultados)
            .Join(db.Proveedores, c => c.ProveedorId, p => p.Id, (c, p) => new CompraResumenDto(
                c.Id, c.ProveedorId, p.RazonSocial, c.NumeroComprobante, c.FechaCompra, c.FormaPago, c.MontoTotal,
                c.Detalles.Count, c.CajaSesionId != null))
            .ToListAsync(ct);
    }
}

public sealed record ObtenerCompraPorIdQuery(Guid Id) : IRequest<CompraDto>;

public sealed class ObtenerCompraPorIdQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerCompraPorIdQuery, CompraDto>
{
    public async Task<CompraDto> Handle(ObtenerCompraPorIdQuery request, CancellationToken ct)
    {
        var compra = await db.Compras.AsNoTracking().Include(c => c.Detalles)
            .SingleOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException("la compra", request.Id);

        var proveedor = await db.Proveedores.AsNoTracking()
            .Where(p => p.Id == compra.ProveedorId).Select(p => p.RazonSocial).SingleAsync(ct);

        return CompraMapper.ToDto(compra, proveedor);
    }
}

internal static class CompraMapper
{
    public static CompraDto ToDto(Compra c, string proveedor) => new(
        c.Id, c.ProveedorId, proveedor, c.NumeroComprobante, c.FechaCompra, c.FormaPago, c.MontoTotal,
        c.CajaSesionId, c.FechaRegistroUtc, c.Observaciones,
        c.Detalles.Select(d => new CompraDetalleDto(d.ProductoId, d.NombreProducto, d.Cantidad, d.CostoUnitario, d.Subtotal)).ToList());
}
