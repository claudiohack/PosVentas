namespace PosVentas.Domain.Enums;

public enum TipoComprobante
{
    /// <summary>Ticket interno: no es un comprobante fiscal.</summary>
    Ticket = 1,
    FacturaA = 2,
    FacturaB = 3,
    FacturaC = 4,
    NotaCreditoA = 5,
    NotaCreditoB = 6,
    NotaCreditoC = 7
}

/// <summary>Situación de la venta respecto de la facturación electrónica.</summary>
public enum EstadoFacturacion
{
    /// <summary>Ticket interno: no requiere CAE.</summary>
    NoRequerida = 1,

    /// <summary>Se pidió factura pero ARCA todavía no otorgó el CAE (rechazo o falta de conexión).</summary>
    Pendiente = 2,

    /// <summary>Factura autorizada con CAE.</summary>
    Facturada = 3
}

public enum ResultadoComprobanteFiscal
{
    Aprobado = 1,
    Rechazado = 2
}

public enum MedioPago
{
    Efectivo = 1,
    Tarjeta = 2,
    Transferencia = 3,
    QR = 4,

    /// <summary>Venta a crédito: no ingresa dinero a la caja, se carga en la cuenta del cliente.</summary>
    CuentaCorriente = 5
}

public enum TipoMovimientoCaja
{
    /// <summary>Cobro de una venta.</summary>
    Venta = 1,

    /// <summary>Recibo de cobranza de cuenta corriente.</summary>
    Cobranza = 2,

    /// <summary>Egreso: pago al contado de una compra a proveedor (monto negativo).</summary>
    PagoProveedor = 3,

    /// <summary>Egreso: devolución del dinero de una venta anulada (monto negativo).</summary>
    DevolucionAnulacion = 4
}

public enum EstadoVenta
{
    Activa = 1,
    Anulada = 2
}

/// <summary>Situación de la nota de crédito de una anulación.</summary>
public enum EstadoNotaCredito
{
    /// <summary>La venta no tenía factura con CAE: la anulación es solo interna.</summary>
    NoRequerida = 1,

    /// <summary>Falta el CAE de la nota de crédito (ARCA la rechazó o no respondió); se reintenta.</summary>
    Pendiente = 2,

    /// <summary>Nota de crédito autorizada por ARCA.</summary>
    Emitida = 3
}

/// <summary>Cómo se paga una compra de mercadería.</summary>
public enum FormaPagoCompra
{
    /// <summary>Sale efectivo de la caja abierta.</summary>
    Efectivo = 1,

    /// <summary>Transferencia bancaria; queda registrada en el turno de caja como egreso no efectivo.</summary>
    Transferencia = 2,

    /// <summary>A pagar más adelante: no mueve dinero de la caja.</summary>
    CuentaCorrienteProveedor = 3
}

public enum TipoMovimientoCuentaCorriente
{
    /// <summary>Aumenta la deuda del cliente (venta a crédito).</summary>
    Cargo = 1,

    /// <summary>Disminuye la deuda del cliente (recibo de cobranza).</summary>
    Abono = 2
}

public enum CondicionIva
{
    ConsumidorFinal = 1,
    ResponsableInscripto = 2,
    Monotributista = 3,
    Exento = 4
}

/// <summary>Estado de la suscripción de un comercio en un momento dado (se deriva de su vencimiento).</summary>
public enum EstadoSuscripcion
{
    /// <summary>Prueba gratuita vigente.</summary>
    Prueba = 1,

    /// <summary>Pagada y al día.</summary>
    Activa = 2,

    /// <summary>Vencida, dentro de los días de gracia: opera normalmente, con aviso.</summary>
    EnGracia = 3,

    /// <summary>Vencida y sin gracia: puede ingresar, consultar y exportar, pero no registrar ni modificar nada.</summary>
    SoloLectura = 4
}

public enum OrigenPagoSuscripcion
{
    /// <summary>Registrado a mano por un administrador de la plataforma (transferencia, efectivo, cortesía).</summary>
    Manual = 1,

    /// <summary>Débito automático de Mercado Pago.</summary>
    MercadoPago = 2
}
