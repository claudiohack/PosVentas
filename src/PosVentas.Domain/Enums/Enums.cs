namespace PosVentas.Domain.Enums;

public enum RolUsuario
{
    Administrador = 1,
    Cajero = 2
}

public enum TipoComprobante
{
    Ticket = 1,
    FacturaA = 2,
    FacturaB = 3,
    FacturaC = 4
}

public enum MedioPago
{
    Efectivo = 1,
    Tarjeta = 2,
    Transferencia = 3,
    QR = 4
}

public enum TipoMovimientoCaja
{
    /// <summary>Cobro de una venta.</summary>
    Venta = 1
}

public enum CondicionIva
{
    ConsumidorFinal = 1,
    ResponsableInscripto = 2,
    Monotributista = 3,
    Exento = 4
}
