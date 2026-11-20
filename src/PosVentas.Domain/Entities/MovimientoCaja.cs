using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Movimiento de dinero en un turno de caja, discriminado por medio de pago. Ingresos con monto
/// positivo (ventas, cobranzas) y egresos con monto negativo (pagos a proveedores).
/// Base del arqueo: el efectivo esperado al cierre es el fondo inicial más los movimientos en efectivo.
/// </summary>
public sealed class MovimientoCaja : TenantEntity
{
    public const int LongitudMaximaDescripcion = 200;

    public Guid CajaSesionId { get; private set; }
    public TipoMovimientoCaja Tipo { get; private set; }
    public MedioPago Medio { get; private set; }

    /// <summary>Importe neto que quedó en la caja (en efectivo, ya descontado el vuelto).</summary>
    public decimal Monto { get; private set; }

    public Guid? VentaId { get; private set; }
    public Guid? CompraId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public DateTime FechaUtc { get; private set; }
    public string Descripcion { get; private set; } = string.Empty;

    private MovimientoCaja(Guid id) : base(id) { }

    // Requerido por EF Core
    private MovimientoCaja() { }

    /// <summary>Genera un movimiento por cada medio de pago de la venta, en el turno donde se cobró.</summary>
    public static IReadOnlyList<MovimientoCaja> PorVenta(CajaSesion sesion, Venta venta)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        ArgumentNullException.ThrowIfNull(venta);
        if (!sesion.EnCurso)
            throw new DomainException("No se pueden registrar movimientos en una caja cerrada.");
        if (venta.CajaSesionId != sesion.Id)
            throw new DomainException("La venta no pertenece a este turno de caja.");
        if (venta.Pagos.Count == 0)
            throw new DomainException("La venta no tiene pagos registrados.");

        var movimientos = new List<MovimientoCaja>();
        foreach (var pago in venta.Pagos)
        {
            // La cuenta corriente no ingresa dinero a la caja: se registra en la cuenta del cliente.
            if (pago.Medio == MedioPago.CuentaCorriente)
                continue;

            var neto = pago.Medio == MedioPago.Efectivo ? pago.Monto - venta.Vuelto : pago.Monto;
            if (neto <= 0)
                continue;

            movimientos.Add(new MovimientoCaja(Guid.NewGuid())
            {
                CajaSesionId = sesion.Id,
                Tipo = TipoMovimientoCaja.Venta,
                Medio = pago.Medio,
                Monto = neto,
                VentaId = venta.Id,
                UsuarioId = venta.UsuarioId,
                FechaUtc = venta.FechaUtc,
                Descripcion = $"Venta {venta.TipoComprobante}"
            });
        }

        return movimientos;
    }

    /// <summary>
    /// Egreso por el pago al contado de una compra. Se guarda con monto negativo para que
    /// el efectivo esperado del arqueo (fondo + suma de movimientos en efectivo) lo descuente.
    /// </summary>
    internal static MovimientoCaja PorPagoProveedor(CajaSesion sesion, Compra compra)
    {
        if (!sesion.EnCurso)
            throw new DomainException("No se pueden registrar movimientos en una caja cerrada.");
        if (compra.CajaSesionId != sesion.Id)
            throw new DomainException("La compra no está vinculada a este turno de caja.");

        return new MovimientoCaja(Guid.NewGuid())
        {
            CajaSesionId = sesion.Id,
            Tipo = TipoMovimientoCaja.PagoProveedor,
            Medio = compra.FormaPago == FormaPagoCompra.Efectivo ? MedioPago.Efectivo : MedioPago.Transferencia,
            Monto = -compra.MontoTotal,
            CompraId = compra.Id,
            UsuarioId = compra.UsuarioId,
            FechaUtc = compra.FechaRegistroUtc,
            Descripcion = $"Pago a proveedor · comprobante {compra.NumeroComprobante}"
        };
    }

    /// <summary>Egreso por la devolución del dinero de una venta anulada, en el medio en que se cobró (monto negativo).</summary>
    internal static MovimientoCaja PorDevolucion(CajaSesion sesion, Venta venta, MedioPago medio, decimal monto, Guid usuarioId)
    {
        if (!sesion.EnCurso)
            throw new DomainException("No se pueden registrar movimientos en una caja cerrada.");
        if (monto <= 0)
            throw new DomainException("El importe a devolver debe ser mayor a cero.");

        return new MovimientoCaja(Guid.NewGuid())
        {
            CajaSesionId = sesion.Id,
            Tipo = TipoMovimientoCaja.DevolucionAnulacion,
            Medio = medio,
            Monto = -monto,
            VentaId = venta.Id,
            UsuarioId = usuarioId,
            FechaUtc = DateTime.UtcNow,
            Descripcion = "Devolución por anulación de venta"
        };
    }

    /// <summary>Ingreso en caja por un recibo de cobranza de cuenta corriente.</summary>
    internal static MovimientoCaja PorCobranza(CajaSesion sesion, ReciboCobranza recibo)
    {
        if (!sesion.EnCurso)
            throw new DomainException("No se pueden registrar movimientos en una caja cerrada.");
        if (recibo.CajaSesionId != sesion.Id)
            throw new DomainException("El recibo no pertenece a este turno de caja.");

        return new MovimientoCaja(Guid.NewGuid())
        {
            CajaSesionId = sesion.Id,
            Tipo = TipoMovimientoCaja.Cobranza,
            Medio = recibo.Medio,
            Monto = recibo.Monto,
            UsuarioId = recibo.UsuarioId,
            FechaUtc = recibo.FechaUtc,
            Descripcion = "Cobranza de cuenta corriente"
        };
    }
}
