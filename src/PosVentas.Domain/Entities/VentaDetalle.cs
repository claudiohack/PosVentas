using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Renglón de una venta. Congela nombre, precio e IVA del producto al momento de vender.
/// </summary>
public sealed class VentaDetalle : TenantEntity
{
    public Guid VentaId { get; private set; }
    public Guid ProductoId { get; private set; }
    public string NombreProducto { get; private set; } = string.Empty;
    public decimal Cantidad { get; private set; }
    public decimal PrecioUnitario { get; private set; }
    public decimal PorcentajeIva { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal MontoIva { get; private set; }

    public decimal TotalConIva => Subtotal + MontoIva;

    private VentaDetalle(Guid id) : base(id) { }

    // Requerido por EF Core
    private VentaDetalle() { }

    internal static VentaDetalle Crear(Guid ventaId, Producto producto, decimal cantidad)
    {
        if (cantidad <= 0)
            throw new DomainException("La cantidad debe ser mayor a cero.");

        var subtotal = decimal.Round(cantidad * producto.PrecioVenta.Monto, 2, MidpointRounding.AwayFromZero);
        var iva = decimal.Round(subtotal * producto.PorcentajeIva / 100m, 2, MidpointRounding.AwayFromZero);

        return new VentaDetalle(Guid.NewGuid())
        {
            VentaId = ventaId,
            ProductoId = producto.Id,
            NombreProducto = producto.Nombre,
            Cantidad = cantidad,
            PrecioUnitario = producto.PrecioVenta.Monto,
            PorcentajeIva = producto.PorcentajeIva,
            Subtotal = subtotal,
            MontoIva = iva
        };
    }
}
