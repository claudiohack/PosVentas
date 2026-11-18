namespace PosVentas.Client.Models;

public enum MedioPago
{
    Efectivo = 1,
    Tarjeta = 2,
    Transferencia = 3,
    QR = 4
}

public sealed record SesionCajaDto(
    Guid Id,
    Guid CajaId,
    string Caja,
    Guid UsuarioAperturaId,
    DateTime FechaAperturaUtc,
    decimal MontoApertura,
    bool EnCurso);

public sealed record CajaDto(Guid Id, string Nombre, bool EstaAbierta, SesionCajaDto? SesionActiva);

public sealed record TotalPorMedioDto(MedioPago Medio, decimal Total);

public sealed record ResumenSesionDto(
    Guid SesionId,
    Guid CajaId,
    string Caja,
    bool EnCurso,
    DateTime FechaAperturaUtc,
    DateTime? FechaCierreUtc,
    decimal MontoApertura,
    int CantidadVentas,
    decimal TotalVendido,
    IReadOnlyList<TotalPorMedioDto> TotalesPorMedio,
    decimal EfectivoEsperado,
    decimal? MontoCierreReal,
    decimal? DiferenciaArqueo,
    string? ResultadoArqueo,
    string? Observaciones);

public sealed record ItemVentaRequest(Guid ProductoId, decimal Cantidad);

public sealed record PagoRequest(MedioPago Medio, decimal Monto);

public sealed record RegistrarVentaRequest(
    Guid CajaSesionId,
    Guid? ClienteId,
    string TipoComprobante,
    IReadOnlyList<ItemVentaRequest> Items,
    IReadOnlyList<PagoRequest> Pagos);

public sealed record VentaDetalleDto(
    Guid ProductoId,
    string NombreProducto,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal PorcentajeIva,
    decimal Subtotal,
    decimal MontoIva,
    decimal Total);

public sealed record VentaPagoDto(MedioPago Medio, decimal Monto);

public sealed record VentaDto(
    Guid Id,
    DateTime FechaUtc,
    Guid CajaSesionId,
    Guid ClienteId,
    string Cliente,
    Guid UsuarioId,
    string TipoComprobante,
    decimal TotalNetoGravado,
    decimal TotalNetoNoGravado,
    decimal TotalIva,
    decimal TotalGeneral,
    decimal TotalPagado,
    decimal Vuelto,
    IReadOnlyList<VentaDetalleDto> Detalles,
    IReadOnlyList<VentaPagoDto> Pagos);

/// <summary>Renglón del carrito en pantalla (antes de cobrar).</summary>
public sealed class RenglonCarrito(ProductoDto producto)
{
    public ProductoDto Producto { get; } = producto;
    public decimal Cantidad { get; set; } = 1;

    // Mismo redondeo que el dominio (2 decimales, alejándose de cero)
    public decimal Subtotal => decimal.Round(Cantidad * Producto.PrecioVenta, 2, MidpointRounding.AwayFromZero);
    public decimal Iva => decimal.Round(Subtotal * Producto.PorcentajeIva / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal Total => Subtotal + Iva;
}

/// <summary>Pago cargado en pantalla.</summary>
public sealed class PagoEnPantalla
{
    public MedioPago Medio { get; set; } = MedioPago.Efectivo;
    public decimal Monto { get; set; }
}
