namespace PosVentas.Client.Models;

public enum MedioPago
{
    Efectivo = 1,
    Tarjeta = 2,
    Transferencia = 3,
    QR = 4,
    CuentaCorriente = 5
}

public enum TipoMovimientoCuentaCorriente
{
    Cargo = 1,
    Abono = 2
}

public enum CondicionIva
{
    ConsumidorFinal = 1,
    ResponsableInscripto = 2,
    Monotributista = 3,
    Exento = 4
}

public sealed record ClienteDto(
    Guid Id,
    string RazonSocial,
    string? Documento,
    CondicionIva CondicionIva,
    string? Telefono,
    string? Email,
    string? Direccion,
    bool PermiteCuentaCorriente,
    decimal LimiteCredito,
    decimal SaldoActual,
    decimal CreditoDisponible,
    bool EsConsumidorFinal,
    bool EstaActivo);

public sealed record MovimientoCuentaCorrienteDto(
    Guid Id,
    DateTime FechaUtc,
    TipoMovimientoCuentaCorriente Tipo,
    decimal Monto,
    decimal SaldoResultante,
    Guid? VentaId,
    Guid? ReciboCobranzaId,
    string Descripcion);

public sealed record CuentaCorrienteDto(ClienteDto Cliente, IReadOnlyList<MovimientoCuentaCorrienteDto> Movimientos);

public sealed record ReciboCobranzaDto(
    Guid Id,
    Guid ClienteId,
    string Cliente,
    Guid CajaSesionId,
    MedioPago Medio,
    decimal Monto,
    decimal SaldoAnterior,
    decimal SaldoResultante,
    DateTime FechaUtc);

public sealed class NuevoClienteRequest
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "La razón social es obligatoria.")]
    [System.ComponentModel.DataAnnotations.StringLength(150)]
    public string RazonSocial { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(20)]
    public string? Documento { get; set; }

    public CondicionIva CondicionIva { get; set; } = CondicionIva.ConsumidorFinal;

    [System.ComponentModel.DataAnnotations.StringLength(30)]
    public string? Telefono { get; set; }

    [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "El email no es válido.")]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(200)]
    public string? Direccion { get; set; }

    public bool PermiteCuentaCorriente { get; set; }

    [System.ComponentModel.DataAnnotations.Range(0, 1_000_000_000, ErrorMessage = "El límite no puede ser negativo.")]
    public decimal LimiteCredito { get; set; }
}

public static class Etiquetas
{
    public static string Medio(MedioPago medio) => medio switch
    {
        MedioPago.CuentaCorriente => "Cuenta corriente",
        _ => medio.ToString()
    };

    public static readonly MedioPago[] MediosCobranza = [MedioPago.Efectivo, MedioPago.Transferencia, MedioPago.QR];
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
    decimal TotalVentasCuentaCorriente,
    int CantidadCobranzas,
    decimal TotalCobranzas,
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
