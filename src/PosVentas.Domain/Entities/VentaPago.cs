using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>Pago solicitado al cobrar (medio + importe entregado por el cliente).</summary>
public readonly record struct PagoSolicitado(MedioPago Medio, decimal Monto);

/// <summary>
/// Importe cobrado con un medio de pago en una venta. Para efectivo es lo entregado por el cliente
/// (el vuelto se registra en <see cref="Venta.Vuelto"/>).
/// </summary>
public sealed class VentaPago : TenantEntity
{
    public Guid VentaId { get; private set; }
    public MedioPago Medio { get; private set; }
    public decimal Monto { get; private set; }

    private VentaPago(Guid id) : base(id) { }

    // Requerido por EF Core
    private VentaPago() { }

    internal static VentaPago Crear(Guid ventaId, MedioPago medio, decimal monto) => new(Guid.NewGuid())
    {
        VentaId = ventaId,
        Medio = medio,
        Monto = monto
    };
}
