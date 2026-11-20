namespace PosVentas.Client.Models;

public enum EstadoVenta
{
    Activa = 1,
    Anulada = 2
}

public enum EstadoNotaCredito
{
    NoRequerida = 1,
    Pendiente = 2,
    Emitida = 3
}

public sealed record VentaHistorialDto(
    Guid Id,
    DateTime FechaUtc,
    string Cliente,
    int Items,
    decimal TotalGeneral,
    string TipoComprobante,
    int? PuntoVenta,
    long? NumeroComprobante,
    EstadoFacturacion EstadoFacturacion,
    EstadoVenta Estado,
    EstadoNotaCredito? EstadoNotaCredito)
{
    public string? NumeroFormateado => NumeroComprobante is { } n ? $"{PuntoVenta:00000}-{n:00000000}" : null;
}

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
    string? NotaCreditoTipo,
    string? NotaCreditoNumero,
    string? NotaCreditoCae);
