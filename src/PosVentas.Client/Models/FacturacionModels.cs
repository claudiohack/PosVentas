using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public enum EstadoFacturacion
{
    NoRequerida = 1,
    Pendiente = 2,
    Facturada = 3
}

/// <summary>Comprobantes que se eligen en el POS (los valores coinciden con TipoComprobante de la API).</summary>
public static class Comprobantes
{
    public const string Ticket = "Ticket";
    public const string FacturaA = "FacturaA";
    public const string FacturaB = "FacturaB";
    public const string FacturaC = "FacturaC";

    public static readonly (string Valor, string Texto)[] DeVenta =
    [
        (Ticket, "Ticket interno"),
        (FacturaA, "Factura A"),
        (FacturaB, "Factura B"),
        (FacturaC, "Factura C")
    ];

    public static readonly (string Valor, string Texto)[] Facturas = DeVenta[1..];

    public static string Texto(string valor) => DeVenta.FirstOrDefault(c => c.Valor == valor).Texto ?? valor;
}

public sealed record EmisorComprobanteDto(
    string RazonSocial, string? Cuit, string? CondicionIva, string? DomicilioFiscal, string? IngresosBrutos, DateOnly? InicioActividades);

public sealed record ReceptorComprobanteDto(string Nombre, string? TipoDocumento, string? NumeroDocumento, string CondicionIva, string? Domicilio);

public sealed record RenglonComprobanteDto(string Descripcion, decimal Cantidad, decimal PrecioUnitario, decimal PorcentajeIva, decimal Importe);

public sealed record IvaComprobanteDto(decimal Porcentaje, decimal BaseImponible, decimal Importe);

public sealed record ComprobanteImpresionDto(
    Guid VentaId,
    string Tipo,
    string Descripcion,
    char? Letra,
    int? CodigoAfip,
    string? Numero,
    DateTime FechaEmision,
    EstadoFacturacion EstadoFacturacion,
    EmisorComprobanteDto Emisor,
    ReceptorComprobanteDto Receptor,
    IReadOnlyList<RenglonComprobanteDto> Renglones,
    bool DiscriminaIva,
    decimal NetoGravado,
    decimal NoGravado,
    IReadOnlyList<IvaComprobanteDto> Iva,
    decimal Total,
    string? Cae,
    DateOnly? VencimientoCae,
    string? QrUrl,
    string? QrPngBase64,
    string Leyenda,
    bool Homologacion,
    bool Anulada = false,
    string? ComprobanteAsociado = null);

public sealed record ConfiguracionFiscalDto(
    string RazonSocial,
    string? Cuit,
    CondicionIva? CondicionIva,
    int? PuntoVenta,
    string? DomicilioFiscal,
    string? IngresosBrutos,
    DateOnly? InicioActividades,
    bool Produccion,
    bool TieneCertificado,
    string? CertificadoSujeto,
    DateTime? CertificadoVenceUtc,
    bool FacturacionConfigurada);

public sealed record EstadoServicioAfipDto(bool ServidoresOk, bool Autenticado, long? UltimoComprobanteAutorizado, bool Produccion, string Detalle);

public sealed class DatosFiscalesFormulario
{
    [Required(ErrorMessage = "Ingresá el CUIT del comercio.")]
    [RegularExpression(@"^\d{2}-?\d{8}-?\d$", ErrorMessage = "El CUIT debe tener 11 dígitos.")]
    public string Cuit { get; set; } = string.Empty;

    public CondicionIva CondicionIva { get; set; } = CondicionIva.ResponsableInscripto;

    [Range(1, 99_998, ErrorMessage = "El punto de venta debe estar entre 1 y 99998.")]
    public int PuntoVenta { get; set; } = 1;

    [StringLength(200)]
    public string? DomicilioFiscal { get; set; }

    [StringLength(30)]
    public string? IngresosBrutos { get; set; }

    public DateOnly? InicioActividades { get; set; }

    public bool Produccion { get; set; }

    public static DatosFiscalesFormulario Desde(ConfiguracionFiscalDto c) => new()
    {
        Cuit = c.Cuit ?? string.Empty,
        CondicionIva = c.CondicionIva ?? CondicionIva.ResponsableInscripto,
        PuntoVenta = c.PuntoVenta ?? 1,
        DomicilioFiscal = c.DomicilioFiscal,
        IngresosBrutos = c.IngresosBrutos,
        InicioActividades = c.InicioActividades,
        Produccion = c.Produccion
    };
}
