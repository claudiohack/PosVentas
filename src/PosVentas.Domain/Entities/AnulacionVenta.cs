using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Anulación total de una venta. <see cref="Registrar"/> revierte todos sus efectos en un solo paso de dominio:
/// reintegra el stock, devuelve el dinero en la caja abierta (egresos por medio de pago) y acredita en la
/// cuenta corriente lo que se había vendido a crédito. La nota de crédito fiscal, si corresponde, se pide después.
/// </summary>
public sealed class AnulacionVenta : TenantEntity
{
    public const int LongitudMinimaMotivo = 5;
    public const int LongitudMaximaMotivo = 300;

    public Guid VentaId { get; private set; }
    public string Motivo { get; private set; } = string.Empty;

    /// <summary>Usuario (administrador o supervisor) que autorizó la anulación.</summary>
    public Guid UsuarioId { get; private set; }

    public DateTime FechaAnulacionUtc { get; private set; }

    /// <summary>Total de la venta revertido (incluye IVA).</summary>
    public decimal MontoTotalRevertido { get; private set; }

    /// <summary>Dinero devuelto desde la caja (todos los medios salvo cuenta corriente).</summary>
    public decimal MontoDevueltoEnCaja { get; private set; }

    /// <summary>Importe acreditado en la cuenta corriente del cliente.</summary>
    public decimal MontoAcreditadoCuentaCorriente { get; private set; }

    /// <summary>Turno de caja desde el que se devolvió el dinero (null si no hubo devolución de dinero).</summary>
    public Guid? CajaSesionId { get; private set; }

    public EstadoNotaCredito EstadoNotaCredito { get; private set; }

    private AnulacionVenta(Guid id) : base(id) { }

    // Requerido por EF Core
    private AnulacionVenta() { }

    /// <summary>
    /// Anula la venta y devuelve todo lo que hay que persistir junto, en la misma transacción.
    /// </summary>
    /// <param name="productos">Productos de los renglones de la venta (tracked).</param>
    /// <param name="sesionDevolucion">Turno abierto desde el que sale el dinero; obligatorio si la venta cobró dinero.</param>
    /// <param name="efectivoDisponible">Efectivo que hay en ese turno (fondo + neto de movimientos en efectivo).</param>
    public static ResultadoAnulacion Registrar(
        Venta venta,
        IReadOnlyDictionary<Guid, Producto> productos,
        Cliente cliente,
        CajaSesion? sesionDevolucion,
        decimal efectivoDisponible,
        Guid usuarioId,
        string motivo)
    {
        ArgumentNullException.ThrowIfNull(venta);
        ArgumentNullException.ThrowIfNull(cliente);
        if (cliente.Id != venta.ClienteId)
            throw new DomainException("El cliente indicado no corresponde a la venta.");
        if (usuarioId == Guid.Empty)
            throw new DomainException("El usuario que autoriza la anulación es obligatorio.");
        var motivoLimpio = (motivo ?? string.Empty).Trim();
        if (motivoLimpio.Length is < LongitudMinimaMotivo or > LongitudMaximaMotivo)
            throw new DomainException($"El motivo de la anulación debe tener entre {LongitudMinimaMotivo} y {LongitudMaximaMotivo} caracteres.");

        venta.MarcarAnulada(); // falla si ya estaba anulada

        // 1) Stock: vuelve todo lo vendido
        foreach (var detalle in venta.Detalles)
        {
            if (!productos.TryGetValue(detalle.ProductoId, out var producto))
                throw new DomainException($"Falta el producto '{detalle.NombreProducto}' para reintegrar el stock.");
            producto.ReintegrarStock(detalle.Cantidad);
        }

        // 2) Dinero: un egreso por medio de pago (el efectivo, neto del vuelto que ya se había entregado)
        var devoluciones = venta.Pagos
            .Where(p => p.Medio != MedioPago.CuentaCorriente)
            .Select(p => (p.Medio, Monto: p.Medio == MedioPago.Efectivo ? p.Monto - venta.Vuelto : p.Monto))
            .Where(d => d.Monto > 0)
            .ToList();

        var anulacion = new AnulacionVenta(Guid.NewGuid())
        {
            VentaId = venta.Id,
            Motivo = motivoLimpio,
            UsuarioId = usuarioId,
            FechaAnulacionUtc = DateTime.UtcNow,
            MontoTotalRevertido = venta.TotalGeneral,
            MontoDevueltoEnCaja = devoluciones.Sum(d => d.Monto),
            MontoAcreditadoCuentaCorriente = venta.MontoCuentaCorriente,
            EstadoNotaCredito = venta.EsFiscal ? EstadoNotaCredito.Pendiente : EstadoNotaCredito.NoRequerida
        };

        var egresos = new List<MovimientoCaja>();
        if (devoluciones.Count > 0)
        {
            if (sesionDevolucion is null || !sesionDevolucion.EnCurso)
                throw new DomainException("Para devolver el dinero de la venta tiene que haber una caja abierta.");

            var efectivo = devoluciones.Where(d => d.Medio == MedioPago.Efectivo).Sum(d => d.Monto);
            if (efectivo > efectivoDisponible)
                throw new DomainException(
                    $"El efectivo en caja ({efectivoDisponible:0.00}) no alcanza para devolver {efectivo:0.00}.");

            anulacion.CajaSesionId = sesionDevolucion.Id;
            sesionDevolucion.RegistrarDevolucion(anulacion.MontoDevueltoEnCaja);
            egresos.AddRange(devoluciones.Select(d =>
                MovimientoCaja.PorDevolucion(sesionDevolucion, venta, d.Medio, d.Monto, usuarioId)));
        }

        // 3) Cuenta corriente: se acredita lo que se había cargado a crédito
        var ajuste = venta.MontoCuentaCorriente > 0
            ? cliente.RevertirCargo(venta.MontoCuentaCorriente, venta.Id, usuarioId)
            : null;

        return new ResultadoAnulacion(anulacion, egresos, ajuste);
    }

    public void RegistrarNotaCreditoEmitida()
    {
        if (EstadoNotaCredito != EstadoNotaCredito.Pendiente)
            throw new DomainException("La anulación no tiene una nota de crédito pendiente.");
        EstadoNotaCredito = EstadoNotaCredito.Emitida;
        MarcarModificado();
    }
}

public sealed record ResultadoAnulacion(
    AnulacionVenta Anulacion,
    IReadOnlyList<MovimientoCaja> EgresosCaja,
    MovimientoCuentaCorriente? AjusteCuentaCorriente);
