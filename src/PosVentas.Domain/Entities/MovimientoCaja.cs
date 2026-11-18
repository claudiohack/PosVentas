using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Ingreso de dinero en un turno de caja, discriminado por medio de pago.
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
}
