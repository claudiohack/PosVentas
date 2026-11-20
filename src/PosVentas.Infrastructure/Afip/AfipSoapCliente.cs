using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;

namespace PosVentas.Infrastructure.Afip;

/// <summary>Credenciales vigentes de WSAA para operar con WSFEv1.</summary>
public sealed record CredencialesWsfe(string Token, string Sign, string Cuit);

public sealed record RespuestaWsaa(string Token, string Sign, DateTime VenceUtc);

public sealed record RespuestaCae(string Resultado, long? Numero, string? Cae, DateOnly? Vencimiento,
    IReadOnlyList<MensajeAfip> Errores, IReadOnlyList<MensajeAfip> Observaciones);

public sealed record ComprobanteConsultado(long Numero, string? Cae, DateOnly? Vencimiento, decimal ImporteTotal, long DocNro, string Resultado);

/// <summary>Errores de autenticación de WSFE: el token/sign venció o no corresponde (hay que pedir otro a WSAA).</summary>
public sealed class CredencialesAfipInvalidasException(string message) : Exception(message);

/// <summary>
/// Cliente SOAP mínimo de WSAA (LoginCms) y WSFEv1, armado con XML a mano para no depender
/// de proxies WCF generados. Ninguna credencial se escribe en logs.
/// </summary>
public sealed class AfipSoapCliente(HttpClient http, IOptions<AfipOptions> opciones)
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Fe = "http://ar.gov.afip.dif.FEV1/";
    private static readonly XNamespace Wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    /// <summary>Códigos de WSFE que indican token/sign inválido o vencido.</summary>
    private static readonly HashSet<int> ErroresDeAutenticacion = [600, 601, 602];

    // ---------- WSAA ----------

    public async Task<RespuestaWsaa> LoginAsync(X509Certificate2 certificado, bool produccion, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var tra = new XElement("loginTicketRequest", new XAttribute("version", "1.0"),
            new XElement("header",
                new XElement("uniqueId", (uint)ahora.ToUnixTimeSeconds()),
                new XElement("generationTime", ahora.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
                new XElement("expirationTime", ahora.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))),
            new XElement("service", "wsfe"));

        var cms = FirmarCms(Encoding.UTF8.GetBytes(tra.ToString(SaveOptions.DisableFormatting)), certificado);

        var sobre = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap),
            new XAttribute(XNamespace.Xmlns + "wsaa", Wsaa),
            new XElement(Soap + "Body", new XElement(Wsaa + "loginCms", new XElement(Wsaa + "in0", cms)))));

        var respuesta = await EnviarAsync(opciones.Value.Wsaa(produccion), "\"\"", sobre, ct);

        var fault = respuesta.Descendants("faultstring").FirstOrDefault()?.Value;
        if (fault is not null)
        {
            var codigo = respuesta.Descendants("faultcode").FirstOrDefault()?.Value ?? string.Empty;
            if (codigo.Contains("alreadyAuthenticated", StringComparison.OrdinalIgnoreCase))
                throw new AfipNoDisponibleException(
                    "ARCA indica que ya hay un ticket de acceso vigente para este certificado. Reintentá en unos minutos.");

            // cms.cert.untrusted, cms.cert.expired, cms.sign.invalid, cms.bad, ns1:coe.notAuthorized...:
            // problemas del certificado o de su autorización para wsfe → configuración (422), no reintentable.
            if (codigo.Contains("cms.", StringComparison.OrdinalIgnoreCase)
                || codigo.Contains("cert", StringComparison.OrdinalIgnoreCase)
                || codigo.Contains("notAuthorized", StringComparison.OrdinalIgnoreCase))
                throw new Domain.Common.DomainException(
                    $"ARCA rechazó el certificado del comercio: {fault}. Verificá que sea el emitido por ARCA y que esté asociado al servicio wsfe.");

            throw new AfipNoDisponibleException($"WSAA rechazó la autenticación: {fault}");
        }

        var retorno = respuesta.Descendants().FirstOrDefault(e => e.Name.LocalName == "loginCmsReturn")?.Value
            ?? throw new AfipNoDisponibleException("Respuesta inesperada de WSAA.");

        var ticket = XDocument.Parse(retorno);
        var token = ticket.Descendants("token").Single().Value;
        var sign = ticket.Descendants("sign").Single().Value;
        var vence = DateTimeOffset.Parse(ticket.Descendants("expirationTime").Single().Value, CultureInfo.InvariantCulture).UtcDateTime;
        return new RespuestaWsaa(token, sign, vence);
    }

    /// <summary>CMS (PKCS#7) firmado con SHA-256 que contiene el TRA, en Base64.</summary>
    public static string FirmarCms(byte[] contenido, X509Certificate2 certificado)
    {
        var cms = new SignedCms(new ContentInfo(contenido), detached: false);
        var firmante = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificado)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly
        };
        cms.ComputeSignature(firmante);
        return Convert.ToBase64String(cms.Encode());
    }

    // ---------- WSFEv1 ----------

    public async Task<bool> DummyAsync(bool produccion, CancellationToken ct)
    {
        var resultado = await LlamarWsfeAsync(produccion, "FEDummy", [], ct);
        return new[] { "AppServer", "DbServer", "AuthServer" }
            .All(n => string.Equals(resultado.Element(Fe + n)?.Value, "OK", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<long> UltimoAutorizadoAsync(CredencialesWsfe cred, bool produccion, int puntoVenta, int tipo, CancellationToken ct)
    {
        var resultado = await LlamarWsfeAsync(produccion, "FECompUltimoAutorizado",
            [Auth(cred), new XElement(Fe + "PtoVta", puntoVenta), new XElement(Fe + "CbteTipo", tipo)], ct);

        VerificarErroresDeAutenticacion(resultado);
        var errores = Mensajes(resultado.Element(Fe + "Errors"), "Err");
        if (errores.Count > 0)
            throw new AfipNoDisponibleException("ARCA no informó el último comprobante: " + string.Join(" | ", errores));

        return long.Parse(resultado.Element(Fe + "CbteNro")?.Value ?? "0", CultureInfo.InvariantCulture);
    }

    public async Task<RespuestaCae> SolicitarCaeAsync(
        CredencialesWsfe cred, SolicitudComprobanteAfip s, int tipo, long numero, CancellationToken ct)
    {
        var detalle = new XElement(Fe + "FECAEDetRequest",
            new XElement(Fe + "Concepto", s.Concepto),
            new XElement(Fe + "DocTipo", s.DocTipo),
            new XElement(Fe + "DocNro", s.DocNro),
            new XElement(Fe + "CbteDesde", numero),
            new XElement(Fe + "CbteHasta", numero),
            new XElement(Fe + "CbteFch", s.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new XElement(Fe + "ImpTotal", Importe(s.ImporteTotal)),
            new XElement(Fe + "ImpTotConc", Importe(s.ImporteNoGravado)),
            new XElement(Fe + "ImpNeto", Importe(s.ImporteNeto)),
            new XElement(Fe + "ImpOpEx", Importe(s.ImporteExento)),
            new XElement(Fe + "ImpTrib", Importe(0)),
            new XElement(Fe + "ImpIVA", Importe(s.ImporteIva)),
            new XElement(Fe + "MonId", "PES"),
            new XElement(Fe + "MonCotiz", 1),
            new XElement(Fe + "CondicionIVAReceptorId", s.CondicionIvaReceptor));

        if (s.Asociado is { } asociado)
        {
            detalle.Add(new XElement(Fe + "CbtesAsoc", new XElement(Fe + "CbteAsoc",
                new XElement(Fe + "Tipo", Domain.Fiscal.TiposComprobanteAfip.Codigo(asociado.Tipo)),
                new XElement(Fe + "PtoVta", asociado.PuntoVenta),
                new XElement(Fe + "Nro", asociado.Numero),
                new XElement(Fe + "Cuit", asociado.CuitEmisor),
                new XElement(Fe + "CbteFch", asociado.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))));
        }

        if (s.Iva.Count > 0)
        {
            detalle.Add(new XElement(Fe + "Iva", s.Iva.Select(a => new XElement(Fe + "AlicIva",
                new XElement(Fe + "Id", a.Codigo),
                new XElement(Fe + "BaseImp", Importe(a.BaseImponible)),
                new XElement(Fe + "Importe", Importe(a.Importe))))));
        }

        var resultado = await LlamarWsfeAsync(s.Produccion, "FECAESolicitar",
        [
            Auth(cred),
            new XElement(Fe + "FeCAEReq",
                new XElement(Fe + "FeCabReq",
                    new XElement(Fe + "CantReg", 1),
                    new XElement(Fe + "PtoVta", s.PuntoVenta),
                    new XElement(Fe + "CbteTipo", tipo)),
                new XElement(Fe + "FeDetReq", detalle))
        ], ct);

        VerificarErroresDeAutenticacion(resultado);

        var errores = Mensajes(resultado.Element(Fe + "Errors"), "Err");
        var respuestaDetalle = resultado.Element(Fe + "FeDetResp")?.Element(Fe + "FECAEDetResponse");
        var observaciones = Mensajes(respuestaDetalle?.Element(Fe + "Observaciones"), "Obs");
        var resultadoCabecera = resultado.Element(Fe + "FeCabResp")?.Element(Fe + "Resultado")?.Value;
        var resultadoDetalle = respuestaDetalle?.Element(Fe + "Resultado")?.Value ?? resultadoCabecera ?? "R";

        var cae = respuestaDetalle?.Element(Fe + "CAE")?.Value;
        return new RespuestaCae(
            resultadoDetalle,
            long.TryParse(respuestaDetalle?.Element(Fe + "CbteDesde")?.Value, CultureInfo.InvariantCulture, out var nro) ? nro : null,
            string.IsNullOrWhiteSpace(cae) ? null : cae.Trim(),
            Fecha(respuestaDetalle?.Element(Fe + "CAEFchVto")?.Value),
            errores,
            observaciones);
    }

    public async Task<ComprobanteConsultado?> ConsultarAsync(
        CredencialesWsfe cred, bool produccion, int puntoVenta, int tipo, long numero, CancellationToken ct)
    {
        var resultado = await LlamarWsfeAsync(produccion, "FECompConsultar",
        [
            Auth(cred),
            new XElement(Fe + "FeCompConsReq",
                new XElement(Fe + "CbteTipo", tipo),
                new XElement(Fe + "CbteNro", numero),
                new XElement(Fe + "PtoVta", puntoVenta))
        ], ct);

        var get = resultado.Element(Fe + "ResultGet");
        if (get is null)
            return null;

        return new ComprobanteConsultado(
            numero,
            get.Element(Fe + "CodAutorizacion")?.Value,
            Fecha(get.Element(Fe + "FchVto")?.Value),
            decimal.Parse(get.Element(Fe + "ImpTotal")?.Value ?? "0", CultureInfo.InvariantCulture),
            long.Parse(get.Element(Fe + "DocNro")?.Value ?? "0", CultureInfo.InvariantCulture),
            get.Element(Fe + "Resultado")?.Value ?? string.Empty);
    }

    // ---------- Infraestructura SOAP ----------

    private async Task<XElement> LlamarWsfeAsync(bool produccion, string metodo, XElement[] parametros, CancellationToken ct)
    {
        var sobre = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XAttribute(XNamespace.Xmlns + "ar", Fe),
            new XElement(Soap + "Body", new XElement(Fe + metodo, parametros))));

        var respuesta = await EnviarAsync(opciones.Value.Wsfe(produccion), $"\"{Fe.NamespaceName}{metodo}\"", sobre, ct);

        var fault = respuesta.Descendants("faultstring").FirstOrDefault()?.Value;
        if (fault is not null)
            throw new AfipNoDisponibleException($"WSFE devolvió un error SOAP en {metodo}: {fault}");

        return respuesta.Descendants(Fe + $"{metodo}Result").FirstOrDefault()
            ?? throw new AfipNoDisponibleException($"Respuesta inesperada de WSFE en {metodo}.");
    }

    private async Task<XDocument> EnviarAsync(string url, string soapAction, XDocument sobre, CancellationToken ct)
    {
        using var contenido = new StringContent(sobre.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = contenido };
        request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AfipNoDisponibleException("No se pudo conectar con ARCA.", ex);
        }

        using (respuesta)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            // Los SOAP fault llegan con HTTP 500: se parsean igual para leer el faultstring.
            if (!respuesta.IsSuccessStatusCode && !cuerpo.Contains("Envelope", StringComparison.Ordinal))
                throw new AfipNoDisponibleException($"ARCA respondió HTTP {(int)respuesta.StatusCode}.");

            try
            {
                return XDocument.Parse(cuerpo);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new AfipNoDisponibleException("ARCA devolvió una respuesta que no es XML válido.", ex);
            }
        }
    }

    private static XElement Auth(CredencialesWsfe c) => new(Fe + "Auth",
        new XElement(Fe + "Token", c.Token),
        new XElement(Fe + "Sign", c.Sign),
        new XElement(Fe + "Cuit", c.Cuit));

    private static void VerificarErroresDeAutenticacion(XElement resultado)
    {
        var errores = Mensajes(resultado.Element(Fe + "Errors"), "Err");
        if (errores.Any(e => ErroresDeAutenticacion.Contains(e.Codigo)))
            throw new CredencialesAfipInvalidasException(string.Join(" | ", errores));
    }

    private static List<MensajeAfip> Mensajes(XElement? contenedor, string elemento) =>
        contenedor?.Elements(Fe + elemento)
            .Select(e => new MensajeAfip(
                int.TryParse(e.Element(Fe + "Code")?.Value, CultureInfo.InvariantCulture, out var codigo) ? codigo : 0,
                e.Element(Fe + "Msg")?.Value ?? string.Empty))
            .ToList() ?? [];

    private static string Importe(decimal valor) =>
        decimal.Round(valor, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static DateOnly? Fecha(string? yyyymmdd) =>
        DateOnly.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;
}
