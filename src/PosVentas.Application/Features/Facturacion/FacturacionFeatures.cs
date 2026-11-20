using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;

namespace PosVentas.Application.Features.Facturacion;

// ---------- Facturar a posteriori (o reintentar una factura pendiente) ----------

public sealed record FacturarVentaCommand(Guid VentaId, TipoComprobante TipoComprobante) : IRequest<VentaDto>;

public sealed class FacturarVentaCommandValidator : AbstractValidator<FacturarVentaCommand>
{
    public FacturarVentaCommandValidator()
    {
        RuleFor(x => x.VentaId).NotEmpty();
        RuleFor(x => x.TipoComprobante).Must(TiposComprobanteAfip.EsFactura)
            .WithMessage("Se factura como Factura A, B o C.");
    }
}

public sealed class FacturarVentaCommandHandler(IApplicationDbContext db, ICurrentTenantService tenant, EmisorComprobantesFiscales emisor)
    : IRequestHandler<FacturarVentaCommand, VentaDto>
{
    public async Task<VentaDto> Handle(FacturarVentaCommand request, CancellationToken ct)
    {
        var venta = await db.Ventas.Include(v => v.Detalles).Include(v => v.Pagos)
            .SingleOrDefaultAsync(v => v.Id == request.VentaId, ct)
            ?? throw new NotFoundException(nameof(Venta), request.VentaId);

        if (venta.EstadoFacturacion == EstadoFacturacion.Facturada)
            throw new ConflictException($"La venta ya está facturada ({TiposComprobanteAfip.Descripcion(venta.TipoComprobante)} " +
                                        $"{venta.PuntoVenta:00000}-{venta.NumeroComprobante:00000000}).");

        var cliente = await db.Clientes.SingleAsync(c => c.Id == venta.ClienteId, ct);
        var comercio = await db.Comercios.SingleAsync(c => c.Id == tenant.TenantId, ct);

        emisor.ValidarDatosFiscales(comercio, cliente, request.TipoComprobante, venta.TotalGeneral);

        venta.SolicitarFacturacion(request.TipoComprobante);
        await db.SaveChangesAsync(ct);

        await emisor.EmitirAsync(venta, cliente, comercio);
        return VentaMapper.ToDto(venta, cliente.RazonSocial);
    }
}

// ---------- Comprobante para impresión (ticket fiscal con QR) ----------

public sealed record EmisorComprobanteDto(
    string RazonSocial, string? Cuit, string? CondicionIva, string? DomicilioFiscal, string? IngresosBrutos, DateOnly? InicioActividades);

public sealed record ReceptorComprobanteDto(string Nombre, string? TipoDocumento, string? NumeroDocumento, string CondicionIva, string? Domicilio);

public sealed record RenglonComprobanteDto(string Descripcion, decimal Cantidad, decimal PrecioUnitario, decimal PorcentajeIva, decimal Importe);

public sealed record IvaComprobanteDto(decimal Porcentaje, decimal BaseImponible, decimal Importe);

public sealed record ComprobanteImpresionDto(
    Guid VentaId,
    TipoComprobante Tipo,
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
    bool Anulada,
    string? ComprobanteAsociado);

public sealed record ObtenerComprobanteQuery(Guid VentaId) : IRequest<ComprobanteImpresionDto>;

public sealed class ObtenerComprobanteQueryHandler(IApplicationDbContext db, ICurrentTenantService tenant, IGeneradorQr generadorQr)
    : IRequestHandler<ObtenerComprobanteQuery, ComprobanteImpresionDto>
{
    /// <summary>URL del QR fiscal (RG 4892). ARCA mantiene el dominio afip.gob.ar para la verificación.</summary>
    public const string UrlBaseQr = "https://www.afip.gob.ar/fe/qr/?p=";

    public async Task<ComprobanteImpresionDto> Handle(ObtenerComprobanteQuery request, CancellationToken ct)
    {
        var (venta, cliente, comercio) = await ComprobanteImpresion.CargarAsync(db, tenant, request.VentaId, ct);
        var factura = await db.ComprobantesFiscales.AsNoTracking().SingleOrDefaultAsync(c =>
            c.VentaId == venta.Id && c.ComprobanteAsociadoId == null && c.Resultado == ResultadoComprobanteFiscal.Aprobado, ct);

        return ComprobanteImpresion.Construir(venta, cliente, comercio, factura, generadorQr, comprobanteAsociado: null,
            leyendaSinCae: "Documento no válido como factura");
    }

    /// <summary>JSON del QR fiscal (RG 4892, versión 1), en UTF-8 para codificar en Base64.</summary>
    public static byte[] DatosQr(ComprobanteFiscal c) => ComprobanteImpresion.DatosQr(c);
}

// ---------- Nota de crédito para impresión ----------

public sealed record ObtenerNotaCreditoQuery(Guid VentaId) : IRequest<ComprobanteImpresionDto>;

public sealed class ObtenerNotaCreditoQueryHandler(IApplicationDbContext db, ICurrentTenantService tenant, IGeneradorQr generadorQr)
    : IRequestHandler<ObtenerNotaCreditoQuery, ComprobanteImpresionDto>
{
    public async Task<ComprobanteImpresionDto> Handle(ObtenerNotaCreditoQuery request, CancellationToken ct)
    {
        var (venta, cliente, comercio) = await ComprobanteImpresion.CargarAsync(db, tenant, request.VentaId, ct);
        if (venta.Estado != EstadoVenta.Anulada)
            throw new NotFoundException("la nota de crédito de la venta", request.VentaId);

        var fiscales = await db.ComprobantesFiscales.AsNoTracking()
            .Where(c => c.VentaId == venta.Id && c.Resultado == ResultadoComprobanteFiscal.Aprobado).ToListAsync(ct);
        var factura = fiscales.SingleOrDefault(c => c.ComprobanteAsociadoId == null);
        var nota = fiscales.SingleOrDefault(c => c.ComprobanteAsociadoId != null);

        var asociado = factura is null ? null
            : $"{TiposComprobanteAfip.Descripcion(factura.TipoComprobante)} {factura.PuntoVenta:00000}-{factura.NumeroComprobante:00000000}";
        var leyenda = factura is null
            ? "Comprobante interno de anulación: no es una nota de crédito fiscal"
            : "Nota de crédito pendiente de autorización por ARCA";

        return ComprobanteImpresion.Construir(venta, cliente, comercio, nota, generadorQr, asociado, leyenda, esAnulacion: true);
    }
}

internal static class ComprobanteImpresion
{
    public static async Task<(Venta Venta, Cliente Cliente, Comercio Comercio)> CargarAsync(
        IApplicationDbContext db, ICurrentTenantService tenant, Guid ventaId, CancellationToken ct)
    {
        var venta = await db.Ventas.AsNoTracking().Include(v => v.Detalles)
            .SingleOrDefaultAsync(v => v.Id == ventaId, ct)
            ?? throw new NotFoundException(nameof(Venta), ventaId);
        var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == venta.ClienteId, ct);
        var comercio = await db.Comercios.AsNoTracking().SingleAsync(c => c.Id == tenant.TenantId, ct);
        return (venta, cliente, comercio);
    }

    /// <param name="fiscal">Comprobante aprobado a imprimir (factura o nota de crédito); null = documento interno.</param>
    public static ComprobanteImpresionDto Construir(
        Venta venta, Cliente cliente, Comercio comercio, ComprobanteFiscal? fiscal, IGeneradorQr generadorQr,
        string? comprobanteAsociado, string leyendaSinCae, bool esAnulacion = false)
    {
        var esFactura = fiscal is not null;
        var tipo = esFactura ? fiscal!.TipoComprobante : TipoComprobante.Ticket;
        var discriminaIva = TiposComprobanteAfip.Letra(tipo) == 'A';

        var renglones = venta.Detalles.Select(d => discriminaIva
                ? new RenglonComprobanteDto(d.NombreProducto, d.Cantidad, d.PrecioUnitario, d.PorcentajeIva, d.Subtotal)
                // B, C y ticket: precios finales con IVA incluido
                : new RenglonComprobanteDto(d.NombreProducto, d.Cantidad,
                    d.Cantidad == 0 ? 0 : decimal.Round(d.TotalConIva / d.Cantidad, 2), d.PorcentajeIva, d.TotalConIva))
            .ToList();

        var iva = venta.Detalles.Where(d => d.PorcentajeIva > 0).GroupBy(d => d.PorcentajeIva)
            .Select(g => new IvaComprobanteDto(g.Key, g.Sum(d => d.Subtotal), g.Sum(d => d.MontoIva)))
            .OrderBy(i => i.Porcentaje).ToList();

        var (tipoDoc, nroDoc) = esFactura
            ? (fiscal!.DocTipoReceptor, fiscal.DocNroReceptor)
            : EmisorComprobantesFiscales.DocumentoReceptor(cliente);

        string? qrUrl = null, qrPng = null;
        if (esFactura)
        {
            qrUrl = ObtenerComprobanteQueryHandler.UrlBaseQr + Convert.ToBase64String(DatosQr(fiscal!));
            qrPng = generadorQr.GenerarPngBase64(qrUrl);
        }

        var descripcion = esFactura ? TiposComprobanteAfip.Descripcion(tipo)
            : esAnulacion ? "Comprobante de anulación" : TiposComprobanteAfip.Descripcion(tipo);

        return new ComprobanteImpresionDto(
            venta.Id,
            tipo,
            descripcion,
            esFactura ? TiposComprobanteAfip.Letra(tipo) : null,
            esFactura ? TiposComprobanteAfip.Codigo(tipo) : null,
            esFactura ? $"{fiscal!.PuntoVenta:00000}-{fiscal.NumeroComprobante:00000000}" : null,
            FechaFiscal.ALocal(venta.FechaUtc),
            venta.EstadoFacturacion,
            new EmisorComprobanteDto(comercio.Nombre, comercio.Cuit is null ? null : CuitArgentino.Formatear(comercio.Cuit),
                comercio.CondicionIva?.ToString(), comercio.DomicilioFiscal, comercio.IngresosBrutos, comercio.InicioActividades),
            new ReceptorComprobanteDto(cliente.RazonSocial, DescripcionDocumento(tipoDoc),
                tipoDoc == TiposDocumentoAfip.SinIdentificar ? null
                    : tipoDoc == TiposDocumentoAfip.Cuit ? CuitArgentino.Formatear(nroDoc.ToString()) : nroDoc.ToString(),
                (cliente.EsConsumidorFinalGenerico ? CondicionIva.ConsumidorFinal : cliente.CondicionIva).ToString(),
                cliente.Direccion),
            renglones,
            discriminaIva,
            venta.TotalNetoGravado,
            venta.TotalNetoNoGravado,
            discriminaIva ? iva : [],
            venta.TotalGeneral,
            fiscal?.Cae,
            fiscal?.FechaVencimientoCae,
            qrUrl,
            qrPng,
            esFactura ? "Comprobante autorizado por ARCA" : leyendaSinCae,
            esFactura && !fiscal!.Produccion,
            venta.Estado == EstadoVenta.Anulada,
            comprobanteAsociado);
    }

    /// <summary>JSON del QR fiscal (RG 4892, versión 1), en UTF-8 para codificar en Base64.</summary>
    public static byte[] DatosQr(ComprobanteFiscal c)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("ver", 1);
            json.WriteString("fecha", c.FechaEmision.ToString("yyyy-MM-dd"));
            json.WriteNumber("cuit", long.Parse(c.CuitEmisor));
            json.WriteNumber("ptoVta", c.PuntoVenta);
            json.WriteNumber("tipoCmp", TiposComprobanteAfip.Codigo(c.TipoComprobante));
            json.WriteNumber("nroCmp", c.NumeroComprobante!.Value);
            json.WriteNumber("importe", c.ImporteTotal);
            json.WriteString("moneda", "PES");
            json.WriteNumber("ctz", 1);
            if (c.DocTipoReceptor != TiposDocumentoAfip.SinIdentificar)
            {
                json.WriteNumber("tipoDocRec", c.DocTipoReceptor);
                json.WriteNumber("nroDocRec", c.DocNroReceptor);
            }
            json.WriteString("tipoCodAut", "E");
            json.WriteNumber("codAut", long.Parse(c.Cae!));
            json.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string? DescripcionDocumento(int tipo) => tipo switch
    {
        TiposDocumentoAfip.Cuit => "CUIT",
        TiposDocumentoAfip.Dni => "DNI",
        _ => null
    };
}
