using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Ventas;

/// <summary>
/// Registra una venta de mostrador cobrada en un turno de caja abierto.
/// El UsuarioId sale del JWT (nunca del body). Si no se indica cliente se usa "Consumidor Final".
/// Con TipoComprobante Factura A/B/C (o EsFacturaAfip = true) además se pide el CAE a ARCA.
/// </summary>
public sealed record RegistrarVentaCommand(
    Guid CajaSesionId,
    Guid? ClienteId,
    TipoComprobante TipoComprobante,
    IReadOnlyList<ItemVentaRequest> Items,
    IReadOnlyList<PagoRequest> Pagos,
    bool? EsFacturaAfip = null) : IRequest<VentaDto>
{
    public bool RequiereFactura => TipoComprobante != TipoComprobante.Ticket;
}

public sealed class RegistrarVentaCommandValidator : AbstractValidator<RegistrarVentaCommand>
{
    public const int MaximoItems = 200;
    public const int MaximoPagos = 10;

    public RegistrarVentaCommandValidator()
    {
        RuleFor(x => x.CajaSesionId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEqual(Guid.Empty).When(x => x.ClienteId.HasValue);

        RuleFor(x => x.TipoComprobante)
            .Must(t => t is TipoComprobante.Ticket or TipoComprobante.FacturaA or TipoComprobante.FacturaB or TipoComprobante.FacturaC)
            .WithMessage("Una venta se emite como ticket interno o como factura A, B o C.");
        RuleFor(x => x.EsFacturaAfip)
            .Must((cmd, esFactura) => esFactura is null || esFactura == cmd.RequiereFactura)
            .WithMessage("EsFacturaAfip no coincide con el tipo de comprobante: para factura usá Factura A, B o C; para ticket, Ticket.");

        RuleFor(x => x.Items).NotEmpty().WithMessage("La venta debe tener al menos un ítem.")
            .Must(i => i.Count <= MaximoItems).WithMessage($"La venta no puede superar {MaximoItems} ítems.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductoId).NotEmpty();
            item.RuleFor(i => i.Cantidad).GreaterThan(0).LessThanOrEqualTo(100_000).PrecisionScale(18, 3, true);
        });

        RuleFor(x => x.ClienteId)
            .NotNull()
            .When(x => x.Pagos is not null && x.Pagos.Any(p => p.Medio == MedioPago.CuentaCorriente))
            .WithMessage("Para cobrar con cuenta corriente hay que indicar el cliente.");

        RuleFor(x => x.Pagos).NotEmpty().WithMessage("Debe indicar al menos un medio de pago.")
            .Must(p => p.Count <= MaximoPagos).WithMessage($"No se admiten más de {MaximoPagos} pagos.");
        RuleForEach(x => x.Pagos).ChildRules(pago =>
        {
            pago.RuleFor(p => p.Medio).IsInEnum();
            pago.RuleFor(p => p.Monto).GreaterThan(0).LessThanOrEqualTo(1_000_000_000m).PrecisionScale(18, 2, true);
        });
    }
}

public sealed class RegistrarVentaCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService usuarioActual,
    ICurrentTenantService tenant,
    EmisorComprobantesFiscales emisor)
    : IRequestHandler<RegistrarVentaCommand, VentaDto>
{
    public async Task<VentaDto> Handle(RegistrarVentaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = await UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, cancellationToken);

        // Todas las lecturas pasan por el filtro global: datos de otro comercio no existen (404).
        var sesion = await db.CajaSesiones.SingleOrDefaultAsync(s => s.Id == request.CajaSesionId, cancellationToken)
            ?? throw new NotFoundException("Sesión de caja", request.CajaSesionId);

        var cliente = request.ClienteId is { } clienteId
            ? await db.Clientes.SingleOrDefaultAsync(c => c.Id == clienteId && c.EstaActivo, cancellationToken)
                ?? throw new NotFoundException(nameof(Cliente), clienteId)
            : await db.Clientes.SingleAsync(c => c.EsConsumidorFinalGenerico, cancellationToken);

        // Un renglón por producto (si llega repetido se suman las cantidades)
        var items = request.Items
            .GroupBy(i => i.ProductoId)
            .Select(g => (ProductoId: g.Key, Cantidad: g.Sum(i => i.Cantidad)))
            .ToList();

        var ids = items.Select(i => i.ProductoId).ToList();
        var productos = await db.Productos
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var venta = Venta.Iniciar(cliente.Id, usuarioId, sesion, request.TipoComprobante);

        foreach (var (productoId, cantidad) in items)
        {
            if (!productos.TryGetValue(productoId, out var producto))
                throw new NotFoundException(nameof(Producto), productoId);

            venta.AgregarItem(producto, cantidad); // descuenta stock vía Producto.DescontarStock
        }

        // Valida el límite de crédito si hay pago a cuenta corriente (CreditoRechazadoException → 400)
        venta.RegistrarPagos(request.Pagos.Select(p => new PagoSolicitado(p.Medio, p.Monto)), cliente);
        venta.ValidarParaRegistrar();

        // Factura: los datos fiscales se validan ANTES de persistir (error de datos → 400, sin efectos).
        Comercio? comercio = null;
        if (request.RequiereFactura)
        {
            comercio = await db.Comercios.SingleAsync(c => c.Id == tenant.TenantId, cancellationToken);
            emisor.ValidarDatosFiscales(comercio, cliente, venta.TipoComprobante, venta.TotalGeneral);
        }

        sesion.RegistrarCobro(venta);
        db.Ventas.Add(venta);
        db.MovimientosCaja.AddRange(MovimientoCaja.PorVenta(sesion, venta));

        if (venta.MontoCuentaCorriente > 0)
            db.MovimientosCuentaCorriente.Add(cliente.RegistrarCargo(venta.MontoCuentaCorriente, venta.Id, usuarioId));

        // Una sola transacción: venta, detalles, pagos, stock, movimientos de caja, cuenta corriente y totales del turno.
        // RowVersion en Producto, Cliente y CajaSesion convierte las carreras en 409.
        await db.SaveChangesAsync(cancellationToken);

        // La venta ya está firme (mercadería entregada, dinero en caja). Si ARCA rechaza o no responde,
        // queda pendiente de facturación y se reintenta con POST /api/ventas/{id}/facturar.
        if (comercio is not null)
            await emisor.EmitirAsync(venta, cliente, comercio);

        return VentaMapper.ToDto(venta, cliente.RazonSocial);
    }
}
