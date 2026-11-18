using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Ventas;

public sealed record ItemVentaRequest(Guid ProductoId, decimal Cantidad);

public sealed record PagoRequest(MedioPago Medio, decimal Monto);

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
    TipoComprobante TipoComprobante,
    decimal TotalNetoGravado,
    decimal TotalNetoNoGravado,
    decimal TotalIva,
    decimal TotalGeneral,
    decimal TotalPagado,
    decimal Vuelto,
    IReadOnlyList<VentaDetalleDto> Detalles,
    IReadOnlyList<VentaPagoDto> Pagos);

public sealed record VentaResumenDto(Guid Id, DateTime FechaUtc, string Cliente, int Items, decimal TotalGeneral);
