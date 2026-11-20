using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Resultado de cada solicitud de CAE a ARCA para una venta. Se guardan también los rechazos
/// (con sus códigos) como historial; una venta tiene a lo sumo un comprobante aprobado.
/// </summary>
public sealed class ComprobanteFiscal : TenantEntity
{
    public const int LongitudMaximaObservaciones = 2000;
    public const int LongitudCae = 14;

    public Guid VentaId { get; private set; }
    public TipoComprobante TipoComprobante { get; private set; }

    /// <summary>Para una nota de crédito: la factura aprobada que anula (CbtesAsoc). Null en facturas.</summary>
    public Guid? ComprobanteAsociadoId { get; private set; }
    public int PuntoVenta { get; private set; }

    /// <summary>Número asignado por ARCA (null si el rechazo fue previo a la numeración).</summary>
    public long? NumeroComprobante { get; private set; }

    public string? Cae { get; private set; }
    public DateOnly? FechaVencimientoCae { get; private set; }
    public ResultadoComprobanteFiscal Resultado { get; private set; }

    /// <summary>Errores y observaciones devueltos por ARCA, como "código: mensaje" separados por " | ".</summary>
    public string? ObservacionesAfip { get; private set; }

    // Datos del comprobante necesarios para reimprimir y armar el QR (RG 4892) sin recalcular.
    public string CuitEmisor { get; private set; } = string.Empty;
    public DateOnly FechaEmision { get; private set; }
    public decimal ImporteTotal { get; private set; }
    public int DocTipoReceptor { get; private set; }
    public long DocNroReceptor { get; private set; }
    public bool Produccion { get; private set; }
    public DateTime FechaSolicitudUtc { get; private set; }

    private ComprobanteFiscal(Guid id) : base(id) { }

    // Requerido por EF Core
    private ComprobanteFiscal() { }

    public static ComprobanteFiscal Aprobado(
        Venta venta, TipoComprobante tipo, int puntoVenta, long numero, string cae, DateOnly vencimientoCae,
        string cuitEmisor, DateOnly fechaEmision, int docTipo, long docNro, bool produccion, string? observaciones,
        ComprobanteFiscal? asociado = null)
    {
        if (string.IsNullOrWhiteSpace(cae) || cae.Trim().Length != LongitudCae || !cae.Trim().All(char.IsAsciiDigit))
            throw new DomainException("El CAE devuelto por ARCA no es válido.");
        if (numero <= 0)
            throw new DomainException("El número de comprobante debe ser positivo.");

        var comprobante = Crear(venta, tipo, puntoVenta, cuitEmisor, fechaEmision, docTipo, docNro, produccion, observaciones, asociado);
        comprobante.Resultado = ResultadoComprobanteFiscal.Aprobado;
        comprobante.NumeroComprobante = numero;
        comprobante.Cae = cae.Trim();
        comprobante.FechaVencimientoCae = vencimientoCae;
        return comprobante;
    }

    public static ComprobanteFiscal Rechazado(
        Venta venta, TipoComprobante tipo, int puntoVenta, long? numero, string cuitEmisor, DateOnly fechaEmision,
        int docTipo, long docNro, bool produccion, string observaciones, ComprobanteFiscal? asociado = null)
    {
        var comprobante = Crear(venta, tipo, puntoVenta, cuitEmisor, fechaEmision, docTipo, docNro, produccion, observaciones, asociado);
        comprobante.Resultado = ResultadoComprobanteFiscal.Rechazado;
        comprobante.NumeroComprobante = numero;
        return comprobante;
    }

    private static ComprobanteFiscal Crear(
        Venta venta, TipoComprobante tipo, int puntoVenta, string cuitEmisor, DateOnly fechaEmision,
        int docTipo, long docNro, bool produccion, string? observaciones, ComprobanteFiscal? asociado)
    {
        ArgumentNullException.ThrowIfNull(venta);
        if (tipo == TipoComprobante.Ticket)
            throw new DomainException("Un ticket interno no es un comprobante fiscal.");

        var esNotaCredito = Fiscal.TiposComprobanteAfip.EsNotaCredito(tipo);
        if (esNotaCredito && asociado is null)
            throw new DomainException("La nota de crédito debe indicar la factura que anula.");
        if (!esNotaCredito && asociado is not null)
            throw new DomainException("Solo una nota de crédito lleva comprobante asociado.");
        if (asociado is not null && (asociado.Resultado != ResultadoComprobanteFiscal.Aprobado || asociado.VentaId != venta.Id
                                     || Fiscal.TiposComprobanteAfip.Letra(asociado.TipoComprobante) != Fiscal.TiposComprobanteAfip.Letra(tipo)))
            throw new DomainException("La nota de crédito debe asociarse a la factura aprobada de la misma venta y de la misma letra.");

        var texto = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        if (texto is { Length: > LongitudMaximaObservaciones })
            texto = texto[..LongitudMaximaObservaciones];

        return new ComprobanteFiscal(Guid.NewGuid())
        {
            VentaId = venta.Id,
            TipoComprobante = tipo,
            ComprobanteAsociadoId = asociado?.Id,
            PuntoVenta = puntoVenta,
            CuitEmisor = cuitEmisor,
            FechaEmision = fechaEmision,
            ImporteTotal = venta.TotalGeneral,
            DocTipoReceptor = docTipo,
            DocNroReceptor = docNro,
            Produccion = produccion,
            ObservacionesAfip = texto,
            FechaSolicitudUtc = DateTime.UtcNow
        };
    }
}
