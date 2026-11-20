using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Ventas;

// ---------- Detalle ----------

public sealed record ObtenerVentaPorIdQuery(Guid Id) : IRequest<VentaDto>;

public sealed class ObtenerVentaPorIdQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerVentaPorIdQuery, VentaDto>
{
    public async Task<VentaDto> Handle(ObtenerVentaPorIdQuery request, CancellationToken cancellationToken)
    {
        var venta = await db.Ventas.AsNoTracking()
            .Include(v => v.Detalles)
            .Include(v => v.Pagos)
            .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Venta), request.Id);

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == venta.ClienteId)
            .Select(c => c.RazonSocial)
            .SingleAsync(cancellationToken);

        return VentaMapper.ToDto(venta, cliente);
    }
}

// ---------- Ventas de un turno ----------

public sealed record ObtenerVentasDeSesionQuery(Guid CajaSesionId) : IRequest<IReadOnlyList<VentaResumenDto>>;

public sealed class ObtenerVentasDeSesionQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerVentasDeSesionQuery, IReadOnlyList<VentaResumenDto>>
{
    public async Task<IReadOnlyList<VentaResumenDto>> Handle(ObtenerVentasDeSesionQuery request, CancellationToken cancellationToken)
    {
        if (!await db.CajaSesiones.AnyAsync(s => s.Id == request.CajaSesionId, cancellationToken))
            throw new NotFoundException("Sesión de caja", request.CajaSesionId);

        return await db.Ventas.AsNoTracking()
            .Where(v => v.CajaSesionId == request.CajaSesionId)
            .OrderByDescending(v => v.FechaUtc)
            .Join(db.Clientes, v => v.ClienteId, c => c.Id, (v, c) => new VentaResumenDto(
                v.Id, v.FechaUtc, c.RazonSocial, v.Detalles.Count, v.TotalGeneral,
                v.TipoComprobante, v.EstadoFacturacion, v.NumeroComprobante))
            .ToListAsync(cancellationToken);
    }
}

internal static class VentaMapper
{
    public static VentaDto ToDto(Venta v, string cliente) => new(
        v.Id,
        v.FechaUtc,
        v.CajaSesionId,
        v.ClienteId,
        cliente,
        v.UsuarioId,
        v.TipoComprobante,
        v.TotalNetoGravado,
        v.TotalNetoNoGravado,
        v.TotalIva,
        v.TotalGeneral,
        v.TotalPagado,
        v.Vuelto,
        v.Detalles.Select(d => new VentaDetalleDto(
            d.ProductoId, d.NombreProducto, d.Cantidad, d.PrecioUnitario, d.PorcentajeIva, d.Subtotal, d.MontoIva, d.TotalConIva)).ToList(),
        v.Pagos.OrderBy(p => p.Medio).Select(p => new VentaPagoDto(p.Medio, p.Monto)).ToList(),
        v.EstadoFacturacion,
        v.PuntoVenta,
        v.NumeroComprobante,
        v.Cae,
        v.VencimientoCae);
}
