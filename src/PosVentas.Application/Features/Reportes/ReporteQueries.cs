using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;

namespace PosVentas.Application.Features.Reportes;

// Todas las consultas agregan en SQL Server sobre DbSets con filtro global de tenant:
// un comercio solo ve sus propios números (no hay forma de pedir los de otro).

// ---------- Período ----------

/// <summary>Rango de fechas calendario (hora argentina). Sin fechas = hoy.</summary>
public interface IConsultaPorPeriodo
{
    DateOnly? Desde { get; }
    DateOnly? Hasta { get; }
}

public sealed class PeriodoValidator : AbstractValidator<IConsultaPorPeriodo>
{
    public const int DiasMaximos = 366;

    public PeriodoValidator()
    {
        RuleFor(x => x).Must(x => Periodo.De(x).Desde <= Periodo.De(x).Hasta)
            .WithName("Desde").WithMessage("La fecha 'desde' no puede ser posterior a 'hasta'.");
        RuleFor(x => x).Must(x => Periodo.De(x).Hasta.DayNumber - Periodo.De(x).Desde.DayNumber < DiasMaximos)
            .WithName("Hasta").WithMessage($"El período no puede superar {DiasMaximos} días.");
    }
}

internal readonly record struct Periodo(DateOnly Desde, DateOnly Hasta)
{
    public static Periodo De(IConsultaPorPeriodo consulta)
    {
        var hoy = FechaFiscal.Hoy();
        var hasta = consulta.Hasta ?? (consulta.Desde is { } d && d > hoy ? d : hoy);
        return new Periodo(consulta.Desde ?? hasta, hasta);
    }

    /// <summary>[inicio, fin) en UTC: desde las 00:00 del primer día hasta las 00:00 del día siguiente al último.</summary>
    public DateTime InicioUtc => FechaFiscal.InicioDelDiaUtc(Desde);
    public DateTime FinUtc => FechaFiscal.InicioDelDiaUtc(Hasta.AddDays(1));

    public PeriodoDto ToDto() => new(Desde, Hasta);
}

internal static class Consultas
{
    /// <summary>Ventas vigentes del período: las anuladas no cuentan como venta (se informan aparte).</summary>
    public static IQueryable<Venta> VentasDel(this IApplicationDbContext db, Periodo p) =>
        db.Ventas.Where(v => v.FechaUtc >= p.InicioUtc && v.FechaUtc < p.FinUtc && v.Estado == EstadoVenta.Activa);

    public static IQueryable<VentaDetalle> DetallesDel(this IApplicationDbContext db, Periodo p) =>
        from d in db.VentaDetalles
        join v in db.VentasDel(p) on d.VentaId equals v.Id
        select d;

    public static decimal Pesos(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static decimal Porcentaje(decimal parte, decimal total) =>
        total == 0 ? 0 : decimal.Round(parte * 100m / total, 2, MidpointRounding.AwayFromZero);
}

// ---------- Dashboard ----------

public sealed record GetDashboardResumenQuery(DateOnly? Desde, DateOnly? Hasta) : IRequest<DashboardResumenDto>, IConsultaPorPeriodo;

public sealed class GetDashboardResumenQueryValidator : AbstractValidator<GetDashboardResumenQuery>
{
    public GetDashboardResumenQueryValidator() => Include(new PeriodoValidator());
}

public sealed class GetDashboardResumenQueryHandler(IApplicationDbContext db) : IRequestHandler<GetDashboardResumenQuery, DashboardResumenDto>
{
    public async Task<DashboardResumenDto> Handle(GetDashboardResumenQuery request, CancellationToken ct)
    {
        var periodo = Periodo.De(request);
        var ventas = db.VentasDel(periodo).AsNoTracking();

        var totales = await ventas
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cantidad = g.Count(),
                Total = g.Sum(v => v.TotalGeneral),
                Neto = g.Sum(v => v.TotalNetoGravado + v.TotalNetoNoGravado),
                Iva = g.Sum(v => v.TotalIva),
                Vuelto = g.Sum(v => v.Vuelto),
                Pendientes = g.Count(v => v.EstadoFacturacion == EstadoFacturacion.Pendiente)
            })
            .SingleOrDefaultAsync(ct);

        var costo = await db.DetallesDel(periodo).SumAsync(d => (decimal?)(d.Cantidad * d.CostoUnitario), ct) ?? 0m;

        var pagos = await (
                from p in db.VentaPagos
                join v in ventas on p.VentaId equals v.Id
                group p by p.Medio into g
                select new { Medio = g.Key, Total = g.Sum(x => x.Monto), Operaciones = g.Count() })
            .ToListAsync(ct);

        var stockBajo = await db.Productos.CountAsync(p => p.EstaActivo && p.StockActual <= p.StockMinimo, ct);

        var anuladas = await db.Ventas.AsNoTracking()
            .Where(v => v.FechaUtc >= periodo.InicioUtc && v.FechaUtc < periodo.FinUtc && v.Estado == EstadoVenta.Anulada)
            .GroupBy(_ => 1)
            .Select(g => new { Cantidad = g.Count(), Total = g.Sum(v => v.TotalGeneral) })
            .SingleOrDefaultAsync(ct);

        var cantidad = totales?.Cantidad ?? 0;
        var total = totales?.Total ?? 0m;
        var neto = totales?.Neto ?? 0m;
        var vuelto = totales?.Vuelto ?? 0m;
        var margen = neto - costo;

        // Lo cobrado en efectivo incluye el excedente que se devolvió como vuelto: se informa neto.
        var ingresos = Enum.GetValues<MedioPago>().Select(medio =>
        {
            var fila = pagos.SingleOrDefault(x => x.Medio == medio);
            var importe = (fila?.Total ?? 0m) - (medio == MedioPago.Efectivo ? vuelto : 0m);
            return new IngresoPorMedioDto(medio, Consultas.Pesos(importe), fila?.Operaciones ?? 0, Consultas.Porcentaje(importe, total));
        }).ToList();

        return new DashboardResumenDto(
            periodo.ToDto(),
            cantidad,
            Consultas.Pesos(total),
            Consultas.Pesos(neto),
            Consultas.Pesos(totales?.Iva ?? 0m),
            Consultas.Pesos(costo),
            Consultas.Pesos(margen),
            Consultas.Porcentaje(margen, neto),
            cantidad == 0 ? 0 : Consultas.Pesos(total / cantidad),
            ingresos,
            stockBajo,
            totales?.Pendientes ?? 0,
            anuladas?.Cantidad ?? 0,
            Consultas.Pesos(anuladas?.Total ?? 0m));
    }
}

// ---------- Ranking de productos ----------

public sealed record GetTopProductosVendidosQuery(
    DateOnly? Desde, DateOnly? Hasta, int Top = 10, OrdenRanking OrdenarPor = OrdenRanking.Unidades, Guid? CategoriaId = null)
    : IRequest<TopProductosDto>, IConsultaPorPeriodo;

public sealed class GetTopProductosVendidosQueryValidator : AbstractValidator<GetTopProductosVendidosQuery>
{
    public GetTopProductosVendidosQueryValidator()
    {
        Include(new PeriodoValidator());
        RuleFor(x => x.Top).InclusiveBetween(1, 100);
        RuleFor(x => x.OrdenarPor).IsInEnum();
        RuleFor(x => x.CategoriaId).NotEqual(Guid.Empty).When(x => x.CategoriaId.HasValue);
    }
}

public sealed class GetTopProductosVendidosQueryHandler(IApplicationDbContext db) : IRequestHandler<GetTopProductosVendidosQuery, TopProductosDto>
{
    public async Task<TopProductosDto> Handle(GetTopProductosVendidosQuery request, CancellationToken ct)
    {
        var periodo = Periodo.De(request);
        var detalles = db.DetallesDel(periodo).AsNoTracking();

        if (request.CategoriaId is { } categoriaId)
        {
            // Una categoría de otro comercio no existe para este (404).
            if (!await db.Categorias.AnyAsync(c => c.Id == categoriaId, ct))
                throw new NotFoundException(nameof(Categoria), categoriaId);

            detalles = from d in detalles
                       join p in db.Productos on d.ProductoId equals p.Id
                       where p.CategoriaId == categoriaId
                       select d;
        }

        var agrupado = detalles
            .GroupBy(d => d.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Unidades = g.Sum(d => d.Cantidad),
                Neto = g.Sum(d => d.Subtotal),
                Recaudacion = g.Sum(d => d.Subtotal + d.MontoIva),
                Costo = g.Sum(d => d.Cantidad * d.CostoUnitario),
                Operaciones = g.Count()
            });

        agrupado = request.OrdenarPor == OrdenRanking.Recaudacion
            ? agrupado.OrderByDescending(x => x.Recaudacion).ThenByDescending(x => x.Unidades)
            : agrupado.OrderByDescending(x => x.Unidades).ThenByDescending(x => x.Recaudacion);

        var ranking = await agrupado.Take(request.Top).ToListAsync(ct);

        var ids = ranking.Select(r => r.ProductoId).ToList();
        var productos = await db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Nombre, p.Sku, Categoria = p.Categoria!.Nombre })
            .ToDictionaryAsync(p => p.Id, ct);

        var filas = ranking.Select((r, i) =>
        {
            var p = productos.GetValueOrDefault(r.ProductoId);
            return new TopProductoDto(i + 1, r.ProductoId, p?.Nombre ?? "(producto eliminado)", p?.Sku.Value, p?.Categoria,
                r.Unidades, Consultas.Pesos(r.Recaudacion), Consultas.Pesos(r.Neto), Consultas.Pesos(r.Costo),
                Consultas.Pesos(r.Neto - r.Costo), r.Operaciones);
        }).ToList();

        return new TopProductosDto(periodo.ToDto(), request.OrdenarPor, filas);
    }
}

// ---------- Rentabilidad por categoría ----------

public sealed record GetRentabilidadPorCategoriaQuery(DateOnly? Desde, DateOnly? Hasta)
    : IRequest<RentabilidadPorCategoriaDto>, IConsultaPorPeriodo;

public sealed class GetRentabilidadPorCategoriaQueryValidator : AbstractValidator<GetRentabilidadPorCategoriaQuery>
{
    public GetRentabilidadPorCategoriaQueryValidator() => Include(new PeriodoValidator());
}

public sealed class GetRentabilidadPorCategoriaQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetRentabilidadPorCategoriaQuery, RentabilidadPorCategoriaDto>
{
    public async Task<RentabilidadPorCategoriaDto> Handle(GetRentabilidadPorCategoriaQuery request, CancellationToken ct)
    {
        var periodo = Periodo.De(request);

        // Se agrupa por la categoría actual del producto.
        var agrupado = await (
                from d in db.DetallesDel(periodo)
                join p in db.Productos on d.ProductoId equals p.Id
                group d by p.CategoriaId into g
                select new
                {
                    CategoriaId = g.Key,
                    Unidades = g.Sum(d => d.Cantidad),
                    Neto = g.Sum(d => d.Subtotal),
                    Recaudacion = g.Sum(d => d.Subtotal + d.MontoIva),
                    Costo = g.Sum(d => d.Cantidad * d.CostoUnitario)
                })
            .AsNoTracking()
            .ToListAsync(ct);

        var ids = agrupado.Select(a => a.CategoriaId).ToList();
        var nombres = await db.Categorias.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var netoTotal = agrupado.Sum(a => a.Neto);

        var filas = agrupado
            .Select(a => new RentabilidadCategoriaDto(
                a.CategoriaId,
                nombres.GetValueOrDefault(a.CategoriaId, "(sin categoría)"),
                a.Unidades,
                Consultas.Pesos(a.Recaudacion),
                Consultas.Pesos(a.Neto),
                Consultas.Pesos(a.Costo),
                Consultas.Pesos(a.Neto - a.Costo),
                Consultas.Porcentaje(a.Neto - a.Costo, a.Neto),
                Consultas.Porcentaje(a.Neto, netoTotal)))
            .OrderByDescending(f => f.VentaNeta)
            .ToList();

        return new RentabilidadPorCategoriaDto(periodo.ToDto(), filas);
    }
}

// ---------- Auditoría de cierres de caja ----------

public sealed record GetReporteCierresCajaQuery(DateOnly? Desde, DateOnly? Hasta, Guid? CajaId = null)
    : IRequest<ReporteCierresCajaDto>, IConsultaPorPeriodo;

public sealed class GetReporteCierresCajaQueryValidator : AbstractValidator<GetReporteCierresCajaQuery>
{
    public GetReporteCierresCajaQueryValidator()
    {
        Include(new PeriodoValidator());
        RuleFor(x => x.CajaId).NotEqual(Guid.Empty).When(x => x.CajaId.HasValue);
    }
}

public sealed class GetReporteCierresCajaQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetReporteCierresCajaQuery, ReporteCierresCajaDto>
{
    private const int LimiteTurnos = 500;

    public async Task<ReporteCierresCajaDto> Handle(GetReporteCierresCajaQuery request, CancellationToken ct)
    {
        var periodo = Periodo.De(request);

        if (request.CajaId is { } cajaId && !await db.Cajas.AnyAsync(c => c.Id == cajaId, ct))
            throw new NotFoundException(nameof(Caja), cajaId);

        var cerradas = db.CajaSesiones.AsNoTracking()
            .Where(s => !s.EnCurso && s.FechaCierreUtc >= periodo.InicioUtc && s.FechaCierreUtc < periodo.FinUtc);
        if (request.CajaId is { } filtro)
            cerradas = cerradas.Where(s => s.CajaId == filtro);

        var totales = await cerradas
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cantidad = g.Count(),
                Exactos = g.Count(s => s.DiferenciaArqueo == 0),
                ConSobrante = g.Count(s => s.DiferenciaArqueo > 0),
                ConFaltante = g.Count(s => s.DiferenciaArqueo < 0),
                Sobrantes = g.Sum(s => s.DiferenciaArqueo > 0 ? s.DiferenciaArqueo!.Value : 0m),
                Faltantes = g.Sum(s => s.DiferenciaArqueo < 0 ? s.DiferenciaArqueo!.Value : 0m),
                Vendido = g.Sum(s => s.TotalVendido)
            })
            .SingleOrDefaultAsync(ct);

        var turnos = await cerradas
            .OrderByDescending(s => s.FechaCierreUtc)
            .Take(LimiteTurnos)
            .Select(s => new
            {
                s.Id, s.CajaId, Caja = s.Caja!.Nombre, s.FechaAperturaUtc, s.FechaCierreUtc, s.UsuarioAperturaId, s.UsuarioCierreId,
                s.MontoApertura, s.MontoCierreSistema, s.MontoCierreReal, s.DiferenciaArqueo, s.CantidadVentas, s.TotalVendido,
                s.TotalPagosProveedores, s.Observaciones
            })
            .ToListAsync(ct);

        var usuariosIds = turnos.Select(t => t.UsuarioAperturaId)
            .Concat(turnos.Where(t => t.UsuarioCierreId.HasValue).Select(t => t.UsuarioCierreId!.Value))
            .Distinct().ToList();
        var usuarios = await db.Usuarios.AsNoTracking().Where(u => usuariosIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);

        var filas = turnos.Select(t =>
        {
            var diferencia = t.DiferenciaArqueo ?? 0m;
            return new CierreCajaDto(
                t.Id, t.CajaId, t.Caja, t.FechaAperturaUtc, t.FechaCierreUtc!.Value,
                usuarios.GetValueOrDefault(t.UsuarioAperturaId, "—"),
                t.UsuarioCierreId is { } uc ? usuarios.GetValueOrDefault(uc, "—") : null,
                t.MontoApertura, t.MontoCierreSistema ?? 0m, t.MontoCierreReal ?? 0m, diferencia,
                diferencia switch { > 0 => "Sobrante", < 0 => "Faltante", _ => "Exacto" },
                t.CantidadVentas, t.TotalVendido, t.TotalPagosProveedores, t.Observaciones);
        }).ToList();

        var sobrantes = totales?.Sobrantes ?? 0m;
        var faltantes = totales?.Faltantes ?? 0m;
        return new ReporteCierresCajaDto(
            periodo.ToDto(),
            totales?.Cantidad ?? 0,
            totales?.Exactos ?? 0,
            totales?.ConSobrante ?? 0,
            totales?.ConFaltante ?? 0,
            Consultas.Pesos(sobrantes),
            Consultas.Pesos(-faltantes), // positivo: cuánto faltó en total
            Consultas.Pesos(sobrantes + faltantes),
            Consultas.Pesos(totales?.Vendido ?? 0m),
            filas);
    }
}
