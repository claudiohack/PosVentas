using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;

namespace PosVentas.Application.Features.Facturacion;

public sealed class FacturacionOptions
{
    public const string SectionName = "Facturacion";

    /// <summary>
    /// Importe desde el cual una factura B o C a consumidor final debe identificar al comprador (DNI o CUIT).
    /// Lo fija ARCA por resolución general y se actualiza periódicamente: mantenerlo en configuración.
    /// </summary>
    public decimal MontoMinimoIdentificacionConsumidorFinal { get; set; } = 10_000_000m;
}

/// <summary>
/// Emite el comprobante fiscal de una venta ya registrada: valida datos, pide el CAE a ARCA
/// y guarda el resultado (aprobado o rechazado) en <see cref="ComprobanteFiscal"/>.
/// </summary>
public sealed class EmisorComprobantesFiscales(
    IApplicationDbContext db,
    IAfipService afip,
    IOptions<FacturacionOptions> opciones,
    ILogger<EmisorComprobantesFiscales> logger)
{
    // ARCA numera de forma correlativa por punto de venta y tipo: se serializa la emisión de cada comercio.
    // (En un despliegue con varias instancias habría que reemplazarlo por un lock distribuido.)
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Candados = new();

    /// <summary>
    /// Validaciones previas, antes de persistir nada: configuración del comercio, letra según condición
    /// frente al IVA de emisor y receptor, e identificación del comprador. Errores de datos → 400.
    /// </summary>
    public void ValidarDatosFiscales(Comercio comercio, Cliente cliente, TipoComprobante tipo, decimal total)
    {
        ValidarConfiguracion(comercio);

        var errores = new List<ValidationFailure>();
        void Error(string campo, string mensaje) => errores.Add(new ValidationFailure(campo, mensaje));

        var emisorEsInscripto = comercio.CondicionIva == CondicionIva.ResponsableInscripto;
        if (emisorEsInscripto && tipo is not (TipoComprobante.FacturaA or TipoComprobante.FacturaB))
            Error("TipoComprobante", "Un Responsable Inscripto emite Factura A o B.");
        if (!emisorEsInscripto && tipo != TipoComprobante.FacturaC)
            Error("TipoComprobante", "Un Monotributista emite Factura C.");

        var documento = CuitArgentino.Normalizar(cliente.Documento);
        switch (tipo)
        {
            case TipoComprobante.FacturaA:
                if (cliente.EsConsumidorFinalGenerico)
                    Error("ClienteId", "La Factura A requiere identificar al cliente con su CUIT.");
                else if (cliente.CondicionIva is not (CondicionIva.ResponsableInscripto or CondicionIva.Monotributista))
                    Error("ClienteId", "La Factura A solo se emite a Responsables Inscriptos o Monotributistas.");
                else if (!CuitArgentino.EsValido(documento))
                    Error("ClienteId", $"El CUIT del cliente '{cliente.RazonSocial}' no es válido.");
                break;

            case TipoComprobante.FacturaB:
                if (cliente.CondicionIva == CondicionIva.ResponsableInscripto)
                    Error("ClienteId", "A un Responsable Inscripto le corresponde Factura A.");
                goto default;

            default:
                var identificado = !cliente.EsConsumidorFinalGenerico && DocumentoReceptor(cliente).Tipo != TiposDocumentoAfip.SinIdentificar;
                if (total >= opciones.Value.MontoMinimoIdentificacionConsumidorFinal && !identificado)
                    Error("ClienteId",
                        $"Desde {opciones.Value.MontoMinimoIdentificacionConsumidorFinal:N0} hay que identificar al comprador con DNI o CUIT.");
                if (!cliente.EsConsumidorFinalGenerico && documento.Length > 0 && DocumentoReceptor(cliente).Tipo == TiposDocumentoAfip.SinIdentificar)
                    Error("ClienteId", $"El documento del cliente '{cliente.RazonSocial}' no es un DNI ni un CUIT válido.");
                break;
        }

        if (errores.Count > 0)
            throw new ValidationException(errores);
    }

    /// <summary>El comercio puede emitir comprobantes: datos fiscales completos y certificado vigente (si no, 422).</summary>
    public static void ValidarConfiguracion(Comercio comercio)
    {
        if (!comercio.FacturacionConfigurada)
            throw new DomainException(
                "El comercio no tiene configurada la facturación electrónica (CUIT, condición IVA, punto de venta y certificado).");
        if (comercio.CertificadoAfipVenceUtc <= DateTime.UtcNow)
            throw new DomainException("El certificado de ARCA del comercio está vencido. Cargá uno nuevo en la configuración fiscal.");
    }

    /// <summary>
    /// Pide el CAE para una venta pendiente de facturación ya persistida. Aprobado: registra el comprobante
    /// y lo devuelve. Rechazado: guarda el rechazo y lanza <see cref="AfipRechazoException"/> (400).
    /// Sin comunicación: la venta queda pendiente y se lanza <see cref="AfipNoDisponibleException"/> (503).
    /// </summary>
    public async Task<ComprobanteFiscal> EmitirAsync(Venta venta, Cliente cliente, Comercio comercio)
    {
        // Desde acá no se cancela: si ARCA otorga el CAE hay que registrarlo aunque el cliente HTTP se haya ido.
        var ct = CancellationToken.None;
        var candado = Candados.GetOrAdd(comercio.Id, _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(ct);
        try
        {
            // Otra solicitud concurrente pudo haber facturado esta venta mientras esperábamos.
            if (await db.ComprobantesFiscales.AnyAsync(
                    c => c.VentaId == venta.Id && c.ComprobanteAsociadoId == null && c.Resultado == ResultadoComprobanteFiscal.Aprobado, ct))
                throw new ConflictException("La venta ya fue facturada.");

            var (docTipo, docNro) = DocumentoReceptor(cliente);
            var fecha = FechaFiscal.Hoy();
            var solicitud = ArmarSolicitud(venta, cliente, comercio, venta.TipoComprobante, docTipo, docNro, fecha, asociado: null);

            ResultadoAutorizacionAfip resultado;
            try
            {
                resultado = await afip.AutorizarComprobanteAsync(solicitud, ct);
            }
            catch (AfipNoDisponibleException ex)
            {
                logger.LogWarning(ex, "ARCA no disponible al facturar la venta {VentaId}; queda pendiente.", venta.Id);
                throw new AfipNoDisponibleException(
                    "No se pudo comunicar con ARCA. La venta quedó registrada como pendiente de facturación; reintentá en unos minutos.", ex)
                { VentaId = venta.Id };
            }

            var observaciones = string.Join(" | ", resultado.Errores.Concat(resultado.Observaciones));

            if (!resultado.Aprobado || resultado.Cae is null || resultado.Numero is null || resultado.VencimientoCae is null)
            {
                db.ComprobantesFiscales.Add(ComprobanteFiscal.Rechazado(venta, venta.TipoComprobante, comercio.PuntoVentaAfip!.Value,
                    resultado.Numero, comercio.Cuit!, fecha, docTipo, docNro, comercio.AfipProduccion,
                    observaciones.Length > 0 ? observaciones : "Rechazado sin detalle."));
                await db.SaveChangesAsync(ct);

                var errores = resultado.Errores.Count > 0
                    ? resultado.Errores
                    : resultado.Observaciones.Count > 0 ? resultado.Observaciones : [new MensajeAfip(0, "ARCA rechazó el comprobante sin detalle.")];
                throw new AfipRechazoException(venta.Id, errores);
            }

            var comprobante = ComprobanteFiscal.Aprobado(venta, venta.TipoComprobante, comercio.PuntoVentaAfip!.Value,
                resultado.Numero.Value, resultado.Cae, resultado.VencimientoCae.Value, comercio.Cuit!, fecha,
                docTipo, docNro, comercio.AfipProduccion, observaciones.Length > 0 ? observaciones : null);

            venta.RegistrarAprobacionFiscal(comprobante.PuntoVenta, resultado.Numero.Value, resultado.Cae,
                resultado.VencimientoCae.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            db.ComprobantesFiscales.Add(comprobante);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // ARCA ya autorizó el comprobante: si no se pudo guardar, hay que poder reconstruirlo a mano.
                logger.LogCritical(ex,
                    "CAE otorgado pero no persistido. Venta {VentaId}, tipo {Tipo}, PV {PuntoVenta}, número {Numero}, CAE {Cae}.",
                    venta.Id, venta.TipoComprobante, comprobante.PuntoVenta, resultado.Numero, resultado.Cae);
                throw;
            }

            return comprobante;
        }
        finally
        {
            candado.Release();
        }
    }

    /// <summary>
    /// Pide el CAE de la nota de crédito que anula la factura aprobada de una venta ya anulada (la anulación
    /// está persistida). Mismos importes y receptor que la factura, con la factura como comprobante asociado.
    /// Rechazo → 400 (queda pendiente); sin comunicación → 503 (queda pendiente).
    /// </summary>
    public async Task<ComprobanteFiscal> EmitirNotaCreditoAsync(Venta venta, AnulacionVenta anulacion, Cliente cliente, Comercio comercio)
    {
        var ct = CancellationToken.None;
        ValidarConfiguracion(comercio);

        var candado = Candados.GetOrAdd(comercio.Id, _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(ct);
        try
        {
            var factura = await db.ComprobantesFiscales.SingleOrDefaultAsync(c =>
                    c.VentaId == venta.Id && c.ComprobanteAsociadoId == null && c.Resultado == ResultadoComprobanteFiscal.Aprobado, ct)
                ?? throw new DomainException("La venta no tiene una factura aprobada que anular con nota de crédito.");

            if (await db.ComprobantesFiscales.AnyAsync(
                    c => c.ComprobanteAsociadoId == factura.Id && c.Resultado == ResultadoComprobanteFiscal.Aprobado, ct))
                throw new ConflictException("La nota de crédito de esta venta ya fue emitida.");

            var tipo = TiposComprobanteAfip.NotaCreditoDe(factura.TipoComprobante);
            var fecha = FechaFiscal.Hoy();
            var asociado = new ComprobanteAsociadoAfip(factura.TipoComprobante, factura.PuntoVenta, factura.NumeroComprobante!.Value,
                factura.CuitEmisor, factura.FechaEmision);
            // El receptor tiene que ser el mismo de la factura que se anula.
            var solicitud = ArmarSolicitud(venta, cliente, comercio, tipo, factura.DocTipoReceptor, factura.DocNroReceptor, fecha, asociado);

            ResultadoAutorizacionAfip resultado;
            try
            {
                resultado = await afip.AutorizarComprobanteAsync(solicitud, ct);
            }
            catch (AfipNoDisponibleException ex)
            {
                logger.LogWarning(ex, "ARCA no disponible al emitir la nota de crédito de la venta {VentaId}; queda pendiente.", venta.Id);
                throw new AfipNoDisponibleException(
                    "La venta quedó anulada, pero no se pudo comunicar con ARCA para la nota de crédito. Reintentá en unos minutos.", ex)
                { VentaId = venta.Id };
            }

            var observaciones = string.Join(" | ", resultado.Errores.Concat(resultado.Observaciones));
            if (!resultado.Aprobado || resultado.Cae is null || resultado.Numero is null || resultado.VencimientoCae is null)
            {
                db.ComprobantesFiscales.Add(ComprobanteFiscal.Rechazado(venta, tipo, factura.PuntoVenta, resultado.Numero,
                    factura.CuitEmisor, fecha, factura.DocTipoReceptor, factura.DocNroReceptor, comercio.AfipProduccion,
                    observaciones.Length > 0 ? observaciones : "Rechazado sin detalle.", factura));
                await db.SaveChangesAsync(ct);
                throw new AfipRechazoException(venta.Id,
                    resultado.Errores.Count > 0 ? resultado.Errores : resultado.Observaciones.Count > 0 ? resultado.Observaciones
                        : [new MensajeAfip(0, "ARCA rechazó la nota de crédito sin detalle.")])
                {
                    Detalle = "La venta quedó anulada (stock, caja y cuenta corriente revertidos), pero la nota de crédito quedó pendiente. " +
                              "Reintentá con POST /api/ventas/{id}/nota-credito."
                };
            }

            var notaCredito = ComprobanteFiscal.Aprobado(venta, tipo, factura.PuntoVenta, resultado.Numero.Value, resultado.Cae,
                resultado.VencimientoCae.Value, factura.CuitEmisor, fecha, factura.DocTipoReceptor, factura.DocNroReceptor,
                comercio.AfipProduccion, observaciones.Length > 0 ? observaciones : null, factura);
            db.ComprobantesFiscales.Add(notaCredito);
            anulacion.RegistrarNotaCreditoEmitida();

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "CAE de nota de crédito otorgado pero no persistido. Venta {VentaId}, tipo {Tipo}, PV {PuntoVenta}, número {Numero}, CAE {Cae}.",
                    venta.Id, tipo, notaCredito.PuntoVenta, resultado.Numero, resultado.Cae);
                throw;
            }

            return notaCredito;
        }
        finally
        {
            candado.Release();
        }
    }

    /// <summary>Tipo y número de documento del receptor según la tabla de ARCA.</summary>
    public static (int Tipo, long Numero) DocumentoReceptor(Cliente cliente)
    {
        if (cliente.EsConsumidorFinalGenerico)
            return (TiposDocumentoAfip.SinIdentificar, 0);

        var documento = CuitArgentino.Normalizar(cliente.Documento);
        if (CuitArgentino.EsValido(documento))
            return (TiposDocumentoAfip.Cuit, long.Parse(documento));
        if (documento.Length is 7 or 8)
            return (TiposDocumentoAfip.Dni, long.Parse(documento));
        return (TiposDocumentoAfip.SinIdentificar, 0);
    }

    private static SolicitudComprobanteAfip ArmarSolicitud(
        Venta venta, Cliente cliente, Comercio comercio, TipoComprobante tipo, int docTipo, long docNro, DateOnly fecha,
        ComprobanteAsociadoAfip? asociado)
    {
        var condicionReceptor = cliente.EsConsumidorFinalGenerico
            ? CondicionIvaReceptorAfip.Codigo(CondicionIva.ConsumidorFinal)
            : CondicionIvaReceptorAfip.Codigo(cliente.CondicionIva);

        // Comprobantes C: el monotributista no discrimina IVA; todo el importe va como neto.
        if (TiposComprobanteAfip.Letra(tipo) == 'C')
        {
            return new SolicitudComprobanteAfip(comercio.Id, comercio.Cuit!, comercio.PuntoVentaAfip!.Value, comercio.AfipProduccion,
                tipo, docTipo, docNro, condicionReceptor, fecha,
                ImporteTotal: venta.TotalGeneral, ImporteNoGravado: 0, ImporteNeto: venta.TotalGeneral, ImporteExento: 0,
                ImporteIva: 0, Iva: [], Asociado: asociado);
        }

        var alicuotas = venta.Detalles
            .Where(d => d.PorcentajeIva > 0)
            .GroupBy(d => d.PorcentajeIva)
            .Select(g => new AlicuotaIvaAfip(AlicuotasIvaAfip.Codigo(g.Key), g.Sum(d => d.Subtotal), g.Sum(d => d.MontoIva)))
            .ToList();

        return new SolicitudComprobanteAfip(comercio.Id, comercio.Cuit!, comercio.PuntoVentaAfip!.Value, comercio.AfipProduccion,
            tipo, docTipo, docNro, condicionReceptor, fecha,
            ImporteTotal: venta.TotalGeneral,
            ImporteNoGravado: venta.TotalNetoNoGravado,
            ImporteNeto: venta.TotalNetoGravado,
            ImporteExento: 0,
            ImporteIva: venta.TotalIva,
            Iva: alicuotas,
            Asociado: asociado);
    }
}
