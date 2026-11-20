using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Enums;
using PosVentas.Infrastructure.Afip;

namespace PosVentas.IntegrationTests;

/// <summary>
/// Pruebas del cliente SOAP real de ARCA sin salir a la red: se intercepta el HTTP para verificar
/// el XML enviado y se responden sobres con el formato publicado de WSAA / WSFEv1.
/// </summary>
public sealed class AfipSoapClienteTests
{
    private static readonly XNamespace Fe = "http://ar.gov.afip.dif.FEV1/";
    private static readonly CredencialesWsfe Cred = new("TOKEN", "SIGN", "20111111112");

    [Fact]
    public void El_TRA_se_firma_como_CMS_con_SHA256_e_incluye_el_contenido()
    {
        using var certificado = CertificadoAutofirmado();
        var tra = Encoding.UTF8.GetBytes("<loginTicketRequest version=\"1.0\"><service>wsfe</service></loginTicketRequest>");

        var cms = new SignedCms();
        cms.Decode(Convert.FromBase64String(AfipSoapCliente.FirmarCms(tra, certificado)));
        cms.CheckSignature(verifySignatureOnly: true);

        Assert.Equal(tra, cms.ContentInfo.Content);
        Assert.Equal("2.16.840.1.101.3.4.2.1", cms.SignerInfos[0].DigestAlgorithm.Value);
        Assert.Equal(certificado.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
    }

    [Fact]
    public async Task FECAESolicitar_envia_el_detalle_esperado_y_lee_el_CAE_aprobado()
    {
        var http = new HttpFalso(_ => Sobre("FECAESolicitar", """
            <FeCabResp><Resultado>A</Resultado></FeCabResp>
            <FeDetResp><FECAEDetResponse>
              <Resultado>A</Resultado><CbteDesde>42</CbteDesde><CbteHasta>42</CbteHasta>
              <CAE>75123456789012</CAE><CAEFchVto>20261130</CAEFchVto>
              <Observaciones><Obs><Code>10217</Code><Msg>Observación informativa</Msg></Obs></Observaciones>
            </FECAEDetResponse></FeDetResp>
            """));
        var cliente = Cliente(http);

        var respuesta = await cliente.SolicitarCaeAsync(Cred, Solicitud(), tipo: 6, numero: 42, CancellationToken.None);

        Assert.Equal(("A", 42L, "75123456789012", new DateOnly(2026, 11, 30)), (respuesta.Resultado, respuesta.Numero, respuesta.Cae, respuesta.Vencimiento));
        Assert.Equal(10217, Assert.Single(respuesta.Observaciones).Codigo);

        var enviado = http.Ultimo!;
        Assert.Equal("\"http://ar.gov.afip.dif.FEV1/FECAESolicitar\"", enviado.SoapAction);
        var det = enviado.Xml.Descendants(Fe + "FECAEDetRequest").Single();
        Assert.Equal("6", enviado.Xml.Descendants(Fe + "CbteTipo").First().Value);
        Assert.Equal("42", det.Element(Fe + "CbteDesde")!.Value);
        Assert.Equal("1210.00", det.Element(Fe + "ImpTotal")!.Value);
        Assert.Equal("1000.00", det.Element(Fe + "ImpNeto")!.Value);
        Assert.Equal("210.00", det.Element(Fe + "ImpIVA")!.Value);
        Assert.Equal("5", det.Element(Fe + "CondicionIVAReceptorId")!.Value);
        Assert.Equal("5", det.Descendants(Fe + "AlicIva").Single().Element(Fe + "Id")!.Value);
        Assert.Equal("TOKEN", enviado.Xml.Descendants(Fe + "Token").Single().Value);
    }

    [Fact]
    public async Task Los_errores_de_WSFE_se_devuelven_como_rechazo_y_el_token_vencido_se_distingue()
    {
        var rechazo = Cliente(new HttpFalso(_ => Sobre("FECAESolicitar", """
            <FeCabResp><Resultado>R</Resultado></FeCabResp>
            <FeDetResp><FECAEDetResponse><Resultado>R</Resultado><CbteDesde>42</CbteDesde><CAE></CAE></FECAEDetResponse></FeDetResp>
            <Errors><Err><Code>10016</Code><Msg>El numero no es el proximo a autorizar</Msg></Err></Errors>
            """)));
        var respuesta = await rechazo.SolicitarCaeAsync(Cred, Solicitud(), 6, 42, CancellationToken.None);
        Assert.Equal("R", respuesta.Resultado);
        Assert.Null(respuesta.Cae);
        Assert.Equal(10016, Assert.Single(respuesta.Errores).Codigo);

        var tokenVencido = Cliente(new HttpFalso(_ => Sobre("FECompUltimoAutorizado",
            "<Errors><Err><Code>600</Code><Msg>ValidacionDeToken: No validaron las fechas del token</Msg></Err></Errors>")));
        await Assert.ThrowsAsync<CredencialesAfipInvalidasException>(() =>
            tokenVencido.UltimoAutorizadoAsync(Cred, false, 3, 6, CancellationToken.None));
    }

    [Fact]
    public async Task Un_corte_de_red_se_informa_como_ARCA_no_disponible()
    {
        var caido = Cliente(new HttpFalso(_ => throw new HttpRequestException("Connection reset")));

        await Assert.ThrowsAsync<AfipNoDisponibleException>(() =>
            caido.UltimoAutorizadoAsync(Cred, false, 3, 6, CancellationToken.None));
    }

    [Fact]
    public async Task LoginCms_lee_token_sign_y_vencimiento_del_ticket_de_acceso()
    {
        var ticket = """
            <?xml version="1.0" encoding="UTF-8"?>
            <loginTicketResponse version="1.0"><header><expirationTime>2026-11-21T02:00:00.000-03:00</expirationTime></header>
            <credentials><token>TK</token><sign>SG</sign></credentials></loginTicketResponse>
            """;
        var http = new HttpFalso(_ =>
            $"""<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"><soapenv:Body><loginCmsResponse xmlns="http://wsaa.view.sua.dvadac.desein.afip.gov"><loginCmsReturn>{System.Security.SecurityElement.Escape(ticket)}</loginCmsReturn></loginCmsResponse></soapenv:Body></soapenv:Envelope>""");
        using var certificado = CertificadoAutofirmado();

        var respuesta = await Cliente(http).LoginAsync(certificado, produccion: false, CancellationToken.None);

        Assert.Equal(("TK", "SG"), (respuesta.Token, respuesta.Sign));
        Assert.Equal(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc), respuesta.VenceUtc);
        Assert.StartsWith("https://wsaahomo.afip.gov.ar/", http.Ultimo!.Url);
    }

    [Fact]
    public async Task Certificado_rechazado_por_WSAA_es_un_error_de_configuracion_y_no_reintentable()
    {
        var http = new HttpFalso(_ => """
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"><soapenv:Body><soapenv:Fault>
            <faultcode xmlns:ns1="http://xml.apache.org/axis/">ns1:cms.cert.untrusted</faultcode>
            <faultstring>Certificado no emitido por AC de confianza</faultstring>
            </soapenv:Fault></soapenv:Body></soapenv:Envelope>
            """);
        using var certificado = CertificadoAutofirmado();

        var ex = await Assert.ThrowsAsync<Domain.Common.DomainException>(() => Cliente(http).LoginAsync(certificado, false, CancellationToken.None));
        Assert.Contains("AC de confianza", ex.Message);
    }

    // ---------- Helpers ----------

    private static SolicitudComprobanteAfip Solicitud() => new(
        Guid.NewGuid(), "20111111112", 3, false, TipoComprobante.FacturaB, 99, 0, 5, new DateOnly(2026, 11, 20),
        ImporteTotal: 1210m, ImporteNoGravado: 0m, ImporteNeto: 1000m, ImporteExento: 0m, ImporteIva: 210m,
        Iva: [new AlicuotaIvaAfip(5, 1000m, 210m)]);

    private static AfipSoapCliente Cliente(HttpFalso handler) =>
        new(new HttpClient(handler), Options.Create(new AfipOptions()));

    private static string Sobre(string metodo, string resultado) =>
        $"""<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><{metodo}Response xmlns="http://ar.gov.afip.dif.FEV1/"><{metodo}Result>{resultado}</{metodo}Result></{metodo}Response></soap:Body></soap:Envelope>""";

    private static X509Certificate2 CertificadoAutofirmado()
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=test, SERIALNUMBER=CUIT 20111111112", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificado.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.EphemeralKeySet);
    }

    private sealed record Enviado(string Url, string SoapAction, XDocument Xml);

    private sealed class HttpFalso(Func<HttpRequestMessage, string> responder) : HttpMessageHandler
    {
        public Enviado? Ultimo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cuerpo = await request.Content!.ReadAsStringAsync(ct);
            Ultimo = new Enviado(request.RequestUri!.ToString(),
                request.Headers.TryGetValues("SOAPAction", out var v) ? v.Single() : string.Empty, XDocument.Parse(cuerpo));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responder(request), Encoding.UTF8, "text/xml") };
        }
    }
}
