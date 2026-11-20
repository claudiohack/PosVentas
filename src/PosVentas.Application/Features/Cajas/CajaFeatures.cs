using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Cajas;

// ---------- Listado de cajas con su turno abierto ----------

public sealed record ObtenerCajasQuery : IRequest<IReadOnlyList<CajaDto>>;

public sealed class ObtenerCajasQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerCajasQuery, IReadOnlyList<CajaDto>>
{
    public async Task<IReadOnlyList<CajaDto>> Handle(ObtenerCajasQuery request, CancellationToken cancellationToken)
    {
        var cajas = await db.Cajas.AsNoTracking()
            .Where(c => c.EstaActivo)
            .OrderBy(c => c.Nombre)
            .ToListAsync(cancellationToken);

        var sesionesAbiertas = await db.CajaSesiones.AsNoTracking()
            .Where(s => s.EnCurso)
            .ToDictionaryAsync(s => s.CajaId, cancellationToken);

        return cajas.Select(c => new CajaDto(
                c.Id,
                c.Nombre,
                c.EstaAbierta,
                sesionesAbiertas.TryGetValue(c.Id, out var s) ? CajaMapper.ToDto(s, c.Nombre) : null))
            .ToList();
    }
}

// ---------- Apertura ----------

public sealed record AbrirCajaCommand(Guid CajaId, decimal MontoApertura) : IRequest<SesionCajaDto>;

public sealed class AbrirCajaCommandValidator : AbstractValidator<AbrirCajaCommand>
{
    public AbrirCajaCommandValidator()
    {
        RuleFor(x => x.CajaId).NotEmpty();
        RuleFor(x => x.MontoApertura).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1_000_000_000m).PrecisionScale(18, 2, true);
    }
}

public sealed class AbrirCajaCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<AbrirCajaCommand, SesionCajaDto>
{
    public async Task<SesionCajaDto> Handle(AbrirCajaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = await UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, cancellationToken);

        var caja = await db.Cajas.SingleOrDefaultAsync(c => c.Id == request.CajaId, cancellationToken)
            ?? throw new NotFoundException(nameof(Caja), request.CajaId);

        // El dominio impide abrir dos veces (y un índice único filtrado lo garantiza ante carreras).
        var sesion = CajaSesion.Abrir(caja, usuarioId, request.MontoApertura);
        db.CajaSesiones.Add(sesion);
        await db.SaveChangesAsync(cancellationToken);

        return CajaMapper.ToDto(sesion, caja.Nombre);
    }
}

// ---------- Resumen / vista previa del arqueo ----------

public sealed record ObtenerResumenSesionQuery(Guid SesionId) : IRequest<ResumenSesionDto>;

public sealed class ObtenerResumenSesionQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerResumenSesionQuery, ResumenSesionDto>
{
    public async Task<ResumenSesionDto> Handle(ObtenerResumenSesionQuery request, CancellationToken cancellationToken)
    {
        var sesion = await db.CajaSesiones.AsNoTracking()
            .Include(s => s.Caja)
            .SingleOrDefaultAsync(s => s.Id == request.SesionId, cancellationToken)
            ?? throw new NotFoundException("Sesión de caja", request.SesionId);

        return await ResumenSesionBuilder.ConstruirAsync(db, sesion, cancellationToken);
    }
}

// ---------- Cierre con arqueo ----------

public sealed record CerrarCajaCommand(Guid SesionId, decimal MontoCierreReal, string? Observaciones) : IRequest<ResumenSesionDto>;

public sealed class CerrarCajaCommandValidator : AbstractValidator<CerrarCajaCommand>
{
    public CerrarCajaCommandValidator()
    {
        RuleFor(x => x.SesionId).NotEmpty();
        RuleFor(x => x.MontoCierreReal).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1_000_000_000m).PrecisionScale(18, 2, true);
        RuleFor(x => x.Observaciones).MaximumLength(500);
    }
}

public sealed class CerrarCajaCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<CerrarCajaCommand, ResumenSesionDto>
{
    public async Task<ResumenSesionDto> Handle(CerrarCajaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = await UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, cancellationToken);

        var sesion = await db.CajaSesiones
            .Include(s => s.Caja)
            .SingleOrDefaultAsync(s => s.Id == request.SesionId, cancellationToken)
            ?? throw new NotFoundException("Sesión de caja", request.SesionId);

        var efectivoCobrado = await ResumenSesionBuilder.TotalEfectivoAsync(db, sesion.Id, cancellationToken);

        sesion.Cerrar(sesion.Caja!, usuarioId, efectivoCobrado, request.MontoCierreReal, request.Observaciones);
        await db.SaveChangesAsync(cancellationToken);

        return await ResumenSesionBuilder.ConstruirAsync(db, sesion, cancellationToken);
    }
}

// ---------- Apoyo ----------

internal static class CajaMapper
{
    public static SesionCajaDto ToDto(CajaSesion s, string nombreCaja) =>
        new(s.Id, s.CajaId, nombreCaja, s.UsuarioAperturaId, s.FechaAperturaUtc, s.MontoApertura, s.EnCurso);
}

internal static class ResumenSesionBuilder
{
    public static async Task<decimal> TotalEfectivoAsync(IApplicationDbContext db, Guid sesionId, CancellationToken ct) =>
        await db.MovimientosCaja
            .Where(m => m.CajaSesionId == sesionId && m.Medio == MedioPago.Efectivo)
            .SumAsync(m => (decimal?)m.Monto, ct) ?? 0m;

    public static async Task<ResumenSesionDto> ConstruirAsync(IApplicationDbContext db, CajaSesion sesion, CancellationToken ct)
    {
        // Ingresos (ventas + cobranzas) por medio. Los pagos a proveedores se informan aparte.
        var porMedio = await db.MovimientosCaja.AsNoTracking()
            .Where(m => m.CajaSesionId == sesion.Id
                        && m.Tipo != TipoMovimientoCaja.PagoProveedor && m.Tipo != TipoMovimientoCaja.DevolucionAnulacion)
            .GroupBy(m => m.Medio)
            .Select(g => new { Medio = g.Key, Total = g.Sum(m => m.Monto) })
            .ToListAsync(ct);

        // Dinero que ingresó a la caja (ventas + cobranzas), por medio. La cuenta corriente no es dinero.
        var totales = Enum.GetValues<MedioPago>()
            .Where(m => m != MedioPago.CuentaCorriente)
            .Select(m => new TotalPorMedioDto(m, porMedio.SingleOrDefault(x => x.Medio == m)?.Total ?? 0m))
            .ToList();

        var ventasACredito = await db.VentaPagos.AsNoTracking()
            .Where(p => p.Medio == MedioPago.CuentaCorriente)
            .Join(db.Ventas.Where(v => v.CajaSesionId == sesion.Id), p => p.VentaId, v => v.Id, (p, _) => p.Monto)
            .SumAsync(m => (decimal?)m, ct) ?? 0m;

        // Neto en efectivo: cobros menos pagos a proveedores en efectivo.
        var efectivoNeto = await TotalEfectivoAsync(db, sesion.Id, ct);

        var resultado = sesion.DiferenciaArqueo switch
        {
            null => null,
            > 0 => "Sobrante",
            < 0 => "Faltante",
            _ => "Exacto"
        };

        return new ResumenSesionDto(
            sesion.Id,
            sesion.CajaId,
            sesion.Caja?.Nombre ?? string.Empty,
            sesion.EnCurso,
            sesion.FechaAperturaUtc,
            sesion.FechaCierreUtc,
            sesion.MontoApertura,
            sesion.CantidadVentas,
            sesion.TotalVendido,
            ventasACredito,
            sesion.CantidadCobranzas,
            sesion.TotalCobranzas,
            sesion.CantidadPagosProveedores,
            sesion.TotalPagosProveedores,
            sesion.CantidadDevoluciones,
            sesion.TotalDevoluciones,
            totales,
            sesion.MontoCierreSistema ?? sesion.MontoApertura + efectivoNeto,
            sesion.MontoCierreReal,
            sesion.DiferenciaArqueo,
            resultado,
            sesion.Observaciones);
    }
}

/// <summary>
/// Obtiene el usuario del JWT y confirma que siga activo en su comercio
/// (un token vigente de un usuario dado de baja no puede operar caja ni vender).
/// </summary>
internal static class UsuarioOperador
{
    public static async Task<Guid> ObtenerActivoAsync(IApplicationDbContext db, ICurrentUserService usuarioActual, CancellationToken ct)
    {
        var usuarioId = usuarioActual.UsuarioId
            ?? throw new TenantAccessException("No hay un usuario autenticado.");

        var activo = await db.Usuarios.AnyAsync(u => u.Id == usuarioId && u.EstaActivo, ct);
        if (!activo)
            throw new TenantAccessException("El usuario no está activo en este comercio.");

        return usuarioId;
    }
}
