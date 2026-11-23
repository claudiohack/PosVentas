using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Seguridad;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Anulaciones;

public sealed record AnulacionDto(
    Guid Id,
    Guid VentaId,
    string Motivo,
    Guid UsuarioId,
    string Usuario,
    DateTime FechaAnulacionUtc,
    decimal MontoTotalRevertido,
    decimal MontoDevueltoEnCaja,
    decimal MontoAcreditadoCuentaCorriente,
    Guid? CajaSesionId,
    EstadoNotaCredito EstadoNotaCredito,
    TipoComprobante? NotaCreditoTipo,
    string? NotaCreditoNumero,
    string? NotaCreditoCae);

// ---------- Anulación ----------

/// <param name="CajaSesionId">Turno desde el que se devuelve el dinero. Si se omite: el turno original si sigue abierto,
/// o la única caja abierta.</param>
[RequierePermiso(Permisos.VentasAnular)]
public sealed record AnularVentaCommand(Guid VentaId, string MotivoAnulacion, Guid? CajaSesionId = null) : IRequest<AnulacionDto>;

public sealed class AnularVentaCommandValidator : AbstractValidator<AnularVentaCommand>
{
    public AnularVentaCommandValidator()
    {
        RuleFor(x => x.VentaId).NotEmpty();
        RuleFor(x => x.MotivoAnulacion).NotEmpty()
            .Must(m => m is not null && m.Trim().Length >= AnulacionVenta.LongitudMinimaMotivo)
            .WithMessage($"Indicá el motivo de la anulación (al menos {AnulacionVenta.LongitudMinimaMotivo} caracteres).")
            .MaximumLength(AnulacionVenta.LongitudMaximaMotivo);
        RuleFor(x => x.CajaSesionId).NotEqual(Guid.Empty).When(x => x.CajaSesionId.HasValue);
    }
}

public sealed class AnularVentaCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService usuarioActual,
    ICurrentTenantService tenant,
    EmisorComprobantesFiscales emisor) : IRequestHandler<AnularVentaCommand, AnulacionDto>
{
    public async Task<AnulacionDto> Handle(AnularVentaCommand request, CancellationToken ct)
    {
        var usuarioId = await UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, ct);

        // Una venta de otro comercio no es visible por el filtro global: 404.
        var venta = await db.Ventas.Include(v => v.Detalles).Include(v => v.Pagos)
            .SingleOrDefaultAsync(v => v.Id == request.VentaId, ct)
            ?? throw new NotFoundException(nameof(Venta), request.VentaId);

        if (venta.Estado == EstadoVenta.Anulada)
            throw new ConflictException("La venta ya está anulada.");

        var cliente = await db.Clientes.SingleAsync(c => c.Id == venta.ClienteId, ct);
        var ids = venta.Detalles.Select(d => d.ProductoId).Distinct().ToList();
        var productos = await db.Productos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var sesion = await ResolverSesionAsync(venta, request.CajaSesionId, ct);
        var efectivoDisponible = sesion is null
            ? 0m
            : sesion.MontoApertura + await ResumenSesionBuilder.TotalEfectivoAsync(db, sesion.Id, ct);

        var resultado = AnulacionVenta.Registrar(venta, productos, cliente, sesion, efectivoDisponible, usuarioId, request.MotivoAnulacion);

        db.AnulacionesVenta.Add(resultado.Anulacion);
        db.MovimientosCaja.AddRange(resultado.EgresosCaja);
        if (resultado.AjusteCuentaCorriente is not null)
            db.MovimientosCuentaCorriente.Add(resultado.AjusteCuentaCorriente);

        // Una sola transacción: venta anulada, stock, egresos y totales del turno, cuenta corriente y la anulación.
        // El índice único por VentaId y las RowVersion (Producto, Cliente, CajaSesion) convierten una anulación
        // simultánea o una venta en carrera en 409.
        await db.SaveChangesAsync(ct);

        // La nota de crédito se pide con la anulación ya firme: si ARCA falla, queda pendiente y se reintenta.
        if (resultado.Anulacion.EstadoNotaCredito == EstadoNotaCredito.Pendiente)
        {
            var comercio = await db.Comercios.SingleAsync(c => c.Id == tenant.TenantId, ct);
            await emisor.EmitirNotaCreditoAsync(venta, resultado.Anulacion, cliente, comercio);
        }

        return await AnulacionMapper.ObtenerAsync(db, venta.Id, ct);
    }

    private async Task<CajaSesion?> ResolverSesionAsync(Venta venta, Guid? indicada, CancellationToken ct)
    {
        if (indicada is { } id)
            return await db.CajaSesiones.SingleOrDefaultAsync(s => s.Id == id, ct)
                ?? throw new NotFoundException("Sesión de caja", id);

        var original = await db.CajaSesiones.SingleAsync(s => s.Id == venta.CajaSesionId, ct);
        if (original.EnCurso)
            return original;

        var abiertas = await db.CajaSesiones.Where(s => s.EnCurso).Take(2).ToListAsync(ct);
        return abiertas.Count switch
        {
            0 => null, // el dominio exige caja abierta solo si hay dinero para devolver
            1 => abiertas[0],
            _ => throw new DomainException("Hay más de una caja abierta: indicá desde cuál se devuelve el dinero.")
        };
    }
}

// ---------- Reintento de la nota de crédito ----------

[RequierePermiso(Permisos.VentasAnular)]
public sealed record EmitirNotaCreditoPendienteCommand(Guid VentaId) : IRequest<AnulacionDto>;

public sealed class EmitirNotaCreditoPendienteCommandHandler(
    IApplicationDbContext db, ICurrentTenantService tenant, EmisorComprobantesFiscales emisor)
    : IRequestHandler<EmitirNotaCreditoPendienteCommand, AnulacionDto>
{
    public async Task<AnulacionDto> Handle(EmitirNotaCreditoPendienteCommand request, CancellationToken ct)
    {
        var venta = await db.Ventas.Include(v => v.Detalles).Include(v => v.Pagos)
            .SingleOrDefaultAsync(v => v.Id == request.VentaId, ct)
            ?? throw new NotFoundException(nameof(Venta), request.VentaId);

        var anulacion = await db.AnulacionesVenta.SingleOrDefaultAsync(a => a.VentaId == venta.Id, ct)
            ?? throw new ConflictException("La venta no está anulada.");
        if (anulacion.EstadoNotaCredito != EstadoNotaCredito.Pendiente)
            throw new ConflictException(anulacion.EstadoNotaCredito == EstadoNotaCredito.Emitida
                ? "La nota de crédito ya fue emitida."
                : "La venta no tenía factura: su anulación no requiere nota de crédito.");

        var cliente = await db.Clientes.SingleAsync(c => c.Id == venta.ClienteId, ct);
        var comercio = await db.Comercios.SingleAsync(c => c.Id == tenant.TenantId, ct);
        await emisor.EmitirNotaCreditoAsync(venta, anulacion, cliente, comercio);

        return await AnulacionMapper.ObtenerAsync(db, venta.Id, ct);
    }
}

// ---------- Consulta ----------

public sealed record ObtenerAnulacionQuery(Guid VentaId) : IRequest<AnulacionDto>;

public sealed class ObtenerAnulacionQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerAnulacionQuery, AnulacionDto>
{
    public async Task<AnulacionDto> Handle(ObtenerAnulacionQuery request, CancellationToken ct)
    {
        if (!await db.Ventas.AnyAsync(v => v.Id == request.VentaId, ct))
            throw new NotFoundException(nameof(Venta), request.VentaId);
        if (!await db.AnulacionesVenta.AnyAsync(a => a.VentaId == request.VentaId, ct))
            throw new NotFoundException("la anulación de la venta", request.VentaId);
        return await AnulacionMapper.ObtenerAsync(db, request.VentaId, ct);
    }
}

internal static class AnulacionMapper
{
    public static async Task<AnulacionDto> ObtenerAsync(IApplicationDbContext db, Guid ventaId, CancellationToken ct)
    {
        var a = await db.AnulacionesVenta.AsNoTracking().SingleAsync(x => x.VentaId == ventaId, ct);
        var usuario = await db.Usuarios.AsNoTracking().Where(u => u.Id == a.UsuarioId).Select(u => u.NombreCompleto).SingleAsync(ct);
        var nota = await db.ComprobantesFiscales.AsNoTracking()
            .Where(c => c.VentaId == ventaId && c.ComprobanteAsociadoId != null && c.Resultado == ResultadoComprobanteFiscal.Aprobado)
            .Select(c => new { c.TipoComprobante, c.PuntoVenta, c.NumeroComprobante, c.Cae })
            .SingleOrDefaultAsync(ct);

        return new AnulacionDto(a.Id, a.VentaId, a.Motivo, a.UsuarioId, usuario, a.FechaAnulacionUtc, a.MontoTotalRevertido,
            a.MontoDevueltoEnCaja, a.MontoAcreditadoCuentaCorriente, a.CajaSesionId, a.EstadoNotaCredito,
            nota?.TipoComprobante, nota is null ? null : $"{nota.PuntoVenta:00000}-{nota.NumeroComprobante:00000000}", nota?.Cae);
    }
}
