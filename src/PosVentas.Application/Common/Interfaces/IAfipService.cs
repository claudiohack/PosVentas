using PosVentas.Domain.Enums;

namespace PosVentas.Application.Common.Interfaces;

/// <summary>
/// Puerto hacia ARCA (ex AFIP): autenticación WSAA + factura electrónica WSFEv1.
/// La implementación obtiene el certificado y el ticket de acceso del comercio indicado;
/// la capa de aplicación nunca ve el certificado ni la clave privada.
/// </summary>
public interface IAfipService
{
    /// <summary>
    /// Pide el CAE para el próximo número del punto de venta y tipo indicados.
    /// Un rechazo de ARCA vuelve como resultado (Aprobado = false, con sus códigos);
    /// la falta de comunicación lanza <see cref="Exceptions.AfipNoDisponibleException"/>.
    /// </summary>
    Task<ResultadoAutorizacionAfip> AutorizarComprobanteAsync(SolicitudComprobanteAfip solicitud, CancellationToken ct);

    /// <summary>Prueba la conexión (FEDummy), la autenticación con el certificado y el último número autorizado.</summary>
    Task<EstadoServicioAfip> VerificarConexionAsync(Guid comercioId, TipoComprobante tipo, CancellationToken ct);
}

public sealed record AlicuotaIvaAfip(int Codigo, decimal BaseImponible, decimal Importe);

/// <summary>Comprobante que una nota de crédito anula (CbtesAsoc).</summary>
public sealed record ComprobanteAsociadoAfip(TipoComprobante Tipo, int PuntoVenta, long Numero, string CuitEmisor, DateOnly Fecha);

public sealed record SolicitudComprobanteAfip(
    Guid ComercioId,
    string CuitEmisor,
    int PuntoVenta,
    bool Produccion,
    TipoComprobante Tipo,
    int DocTipo,
    long DocNro,
    int CondicionIvaReceptor,
    DateOnly Fecha,
    decimal ImporteTotal,
    decimal ImporteNoGravado,
    decimal ImporteNeto,
    decimal ImporteExento,
    decimal ImporteIva,
    IReadOnlyList<AlicuotaIvaAfip> Iva,
    ComprobanteAsociadoAfip? Asociado = null)
{
    /// <summary>1 = productos (no requiere fechas de servicio).</summary>
    public int Concepto => 1;
}

public sealed record MensajeAfip(int Codigo, string Mensaje)
{
    public override string ToString() => $"{Codigo}: {Mensaje}";
}

public sealed record ResultadoAutorizacionAfip(
    bool Aprobado,
    long? Numero,
    string? Cae,
    DateOnly? VencimientoCae,
    IReadOnlyList<MensajeAfip> Errores,
    IReadOnlyList<MensajeAfip> Observaciones);

public sealed record EstadoServicioAfip(
    bool ServidoresOk,
    bool Autenticado,
    long? UltimoComprobanteAutorizado,
    bool Produccion,
    string Detalle);

/// <summary>Valida y cifra el certificado digital del comercio (nunca se guarda en claro).</summary>
public interface ICertificadoAfipProtector
{
    CertificadoAfipProcesado ProcesarPkcs12(byte[] contenido, string? password);
    CertificadoAfipProcesado ProcesarPem(string certificadoPem, string clavePrivadaPem, string? password);
}

/// <param name="Protegido">PKCS#12 cifrado con Data Protection, listo para persistir.</param>
/// <param name="CuitDelCertificado">CUIT leído del SERIALNUMBER del sujeto, si el certificado lo trae.</param>
public sealed record CertificadoAfipProcesado(string Protegido, string Sujeto, DateTime VenceUtc, string? CuitDelCertificado);

public interface IGeneradorQr
{
    /// <summary>Imagen PNG del código QR, en Base64.</summary>
    string GenerarPngBase64(string contenido);
}
