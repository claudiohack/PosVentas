using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Reportes;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Ventas;

/// <summary>Ventas del período (más recientes primero), con su estado de facturación y de anulación.</summary>
public sealed record ObtenerHistorialVentasQuery(DateOnly? Desde, DateOnly? Hasta, EstadoVenta? Estado = null)
    : IRequest<IReadOnlyList<VentaHistorialDto>>, IConsultaPorPeriodo;

public sealed class ObtenerHistorialVentasQueryValidator : AbstractValidator<ObtenerHistorialVentasQuery>
{
    public ObtenerHistorialVentasQueryValidator()
    {
        Include(new PeriodoValidator());
        RuleFor(x => x.Estado).IsInEnum().When(x => x.Estado.HasValue);
    }
}

public sealed class ObtenerHistorialVentasQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerHistorialVentasQuery, IReadOnlyList<VentaHistorialDto>>
{
    private const int LimiteResultados = 500;

    public async Task<IReadOnlyList<VentaHistorialDto>> Handle(ObtenerHistorialVentasQuery request, CancellationToken ct)
    {
        var periodo = Periodo.De(request);
        var ventas = db.Ventas.AsNoTracking().Where(v => v.FechaUtc >= periodo.InicioUtc && v.FechaUtc < periodo.FinUtc);
        if (request.Estado is { } estado)
            ventas = ventas.Where(v => v.Estado == estado);

        return await (
                from v in ventas
                join c in db.Clientes on v.ClienteId equals c.Id
                join a in db.AnulacionesVenta on v.Id equals a.VentaId into anulaciones
                from a in anulaciones.DefaultIfEmpty()
                orderby v.FechaUtc descending
                select new VentaHistorialDto(
                    v.Id, v.FechaUtc, c.RazonSocial, v.Detalles.Count, v.TotalGeneral, v.TipoComprobante, v.PuntoVenta,
                    v.NumeroComprobante, v.EstadoFacturacion, v.Estado, a == null ? null : (EstadoNotaCredito?)a.EstadoNotaCredito))
            .Take(LimiteResultados)
            .ToListAsync(ct);
    }
}
