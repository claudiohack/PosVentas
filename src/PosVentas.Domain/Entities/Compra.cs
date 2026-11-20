using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Compra de mercadería a un proveedor (remito o factura). Al agregar cada renglón ingresa el stock
/// y actualiza el costo del producto; al confirmarla, si se pagó al contado con una caja abierta,
/// genera el egreso de caja correspondiente.
/// </summary>
public sealed class Compra : TenantEntity
{
    public const int LongitudMaximaComprobante = 30;
    public const int LongitudMaximaObservaciones = 250;

    private readonly List<CompraDetalle> _detalles = [];

    public Guid ProveedorId { get; private set; }

    /// <summary>Número del remito / factura del proveedor (único por proveedor dentro del comercio).</summary>
    public string NumeroComprobante { get; private set; } = string.Empty;

    /// <summary>Fecha del comprobante del proveedor (puede ser anterior a la carga).</summary>
    public DateOnly FechaCompra { get; private set; }

    public FormaPagoCompra FormaPago { get; private set; }
    public decimal MontoTotal { get; private set; }

    /// <summary>Turno de caja del que salió el pago; null si se compró a cuenta corriente o sin caja abierta.</summary>
    public Guid? CajaSesionId { get; private set; }

    public Guid UsuarioId { get; private set; }
    public DateTime FechaRegistroUtc { get; private set; }
    public string? Observaciones { get; private set; }
    public bool Confirmada { get; private set; }

    public IReadOnlyList<CompraDetalle> Detalles => _detalles;

    private Compra(Guid id) : base(id) { }

    // Requerido por EF Core
    private Compra() { }

    public static Compra Iniciar(
        Proveedor proveedor, string numeroComprobante, DateOnly fechaCompra, FormaPagoCompra formaPago,
        Guid usuarioId, string? observaciones)
    {
        ArgumentNullException.ThrowIfNull(proveedor);
        if (!proveedor.EstaActivo)
            throw new DomainException($"El proveedor '{proveedor.RazonSocial}' está dado de baja.");
        if (string.IsNullOrWhiteSpace(numeroComprobante))
            throw new DomainException("El número de comprobante del proveedor es obligatorio.");
        if (!Enum.IsDefined(formaPago))
            throw new DomainException("La forma de pago no es válida.");
        if (usuarioId == Guid.Empty)
            throw new DomainException("El usuario que registra la compra es obligatorio.");
        if (fechaCompra > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))) // margen por zona horaria
            throw new DomainException("La fecha de compra no puede ser futura.");

        return new Compra(Guid.NewGuid())
        {
            ProveedorId = proveedor.Id,
            NumeroComprobante = NormalizarComprobante(numeroComprobante),
            FechaCompra = fechaCompra,
            FormaPago = formaPago,
            UsuarioId = usuarioId,
            FechaRegistroUtc = DateTime.UtcNow,
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim()
        };
    }

    public static string NormalizarComprobante(string numero) => numero.Trim().ToUpperInvariant();

    /// <summary>Agrega un renglón: ingresa el stock y actualiza el costo del producto en el mismo paso.</summary>
    public CompraDetalle AgregarItem(Producto producto, decimal cantidad, decimal costoUnitario)
    {
        ArgumentNullException.ThrowIfNull(producto);
        if (Confirmada)
            throw new DomainException("La compra ya fue confirmada.");
        if (!producto.EstaActivo)
            throw new DomainException($"El producto '{producto.Nombre}' está dado de baja.");
        if (_detalles.Any(d => d.ProductoId == producto.Id))
            throw new DomainException($"El producto '{producto.Nombre}' está repetido en la compra.");

        producto.IngresarStock(cantidad, costoUnitario); // valida cantidad > 0 y costo >= 0

        var detalle = CompraDetalle.Crear(Id, producto, cantidad, costoUnitario);
        _detalles.Add(detalle);
        MontoTotal += detalle.Subtotal;
        return detalle;
    }

    /// <summary>
    /// Cierra la carga de la compra. Si se pagó al contado y se indica un turno de caja abierto,
    /// lo vincula y devuelve el egreso a registrar; en cualquier otro caso devuelve null.
    /// </summary>
    public MovimientoCaja? Confirmar(CajaSesion? sesionDePago)
    {
        if (Confirmada)
            throw new DomainException("La compra ya fue confirmada.");
        if (_detalles.Count == 0)
            throw new DomainException("La compra debe tener al menos un producto.");

        Confirmada = true;

        if (FormaPago == FormaPagoCompra.CuentaCorrienteProveedor || sesionDePago is null || MontoTotal == 0)
            return null;

        CajaSesionId = sesionDePago.Id;
        sesionDePago.RegistrarPagoProveedor(this);
        return MovimientoCaja.PorPagoProveedor(sesionDePago, this);
    }
}

public sealed class CompraDetalle : TenantEntity
{
    public Guid CompraId { get; private set; }
    public Guid ProductoId { get; private set; }

    /// <summary>Nombre al momento de la compra (histórico).</summary>
    public string NombreProducto { get; private set; } = string.Empty;

    public decimal Cantidad { get; private set; }
    public decimal CostoUnitario { get; private set; }
    public decimal Subtotal { get; private set; }

    private CompraDetalle(Guid id) : base(id) { }

    // Requerido por EF Core
    private CompraDetalle() { }

    internal static CompraDetalle Crear(Guid compraId, Producto producto, decimal cantidad, decimal costoUnitario) => new(Guid.NewGuid())
    {
        CompraId = compraId,
        ProductoId = producto.Id,
        NombreProducto = producto.Nombre,
        Cantidad = cantidad,
        CostoUnitario = costoUnitario,
        Subtotal = decimal.Round(cantidad * costoUnitario, 2, MidpointRounding.AwayFromZero)
    };
}
