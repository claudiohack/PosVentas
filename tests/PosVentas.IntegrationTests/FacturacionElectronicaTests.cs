using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Clientes;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;
using PosVentas.Infrastructure.Persistence;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class FacturacionElectronicaTests(PosVentasApiFactory factory)
{
    private const string CuitInscripto = "20111111112";
    private const string CuitMonotributista = "27222222228";
    private const string CuitClienteInscripto = "30712345671";
    private const int PuntoVenta = 3;

    private AfipServiceFalso Afip => factory.Services.GetRequiredService<AfipServiceFalso>();

    [Fact]
    public async Task Factura_B_a_consumidor_final_obtiene_CAE_y_registra_el_comprobante_fiscal_del_tenant()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        var respuesta = await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m, esFacturaAfip: true);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var venta = (await respuesta.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(EstadoFacturacion.Facturada, venta.EstadoFacturacion);
        Assert.Equal(PuntoVenta, venta.PuntoVenta);
        Assert.Equal(1, venta.NumeroComprobante);
        Assert.Matches("^[0-9]{14}$", venta.Cae);

        // Lo que se le pidió a ARCA: Factura B (6) a consumidor final sin identificar, IVA 21 % discriminado
        var solicitud = Afip.SolicitudesDe(a.TenantId).Last();
        Assert.Equal((TipoComprobante.FacturaB, 99, 0L, 5), (solicitud.Tipo, solicitud.DocTipo, solicitud.DocNro, solicitud.CondicionIvaReceptor));
        Assert.Equal((1210m, 1000m, 210m), (solicitud.ImporteTotal, solicitud.ImporteNeto, solicitud.ImporteIva));
        var alicuota = Assert.Single(solicitud.Iva);
        Assert.Equal((5, 1000m, 210m), (alicuota.Codigo, alicuota.BaseImponible, alicuota.Importe));

        // ComprobanteFiscal persistido, asociado al tenant A
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var comprobante = await db.ComprobantesFiscales.IgnoreQueryFilters().SingleAsync(c => c.VentaId == venta.Id);
            Assert.Equal(a.TenantId, comprobante.TenantId);
            Assert.Equal(ResultadoComprobanteFiscal.Aprobado, comprobante.Resultado);
            Assert.Equal((TipoComprobante.FacturaB, PuntoVenta, 1L, venta.Cae), (comprobante.TipoComprobante, comprobante.PuntoVenta, comprobante.NumeroComprobante, comprobante.Cae));
        }

        // La numeración es correlativa
        var segunda = (await (await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m)).Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(2, segunda.NumeroComprobante);

        // Comprobante para impresión con el QR fiscal (RG 4892)
        var impresion = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{venta.Id}/comprobante", ApiTestHelpers.Json))!;
        Assert.Equal("Factura B", impresion.Descripcion);
        Assert.Equal("00003-00000001", impresion.Numero);
        Assert.Equal(venta.Cae, impresion.Cae);
        Assert.False(impresion.DiscriminaIva);
        Assert.Equal("20-11111111-2", impresion.Emisor.Cuit);
        Assert.StartsWith("https://www.afip.gob.ar/fe/qr/?p=", impresion.QrUrl);
        Assert.StartsWith("iVBOR", impresion.QrPngBase64); // firma de un PNG en Base64

        var qr = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(impresion.QrUrl!.Split("?p=")[1])))!;
        Assert.Equal(1, qr["ver"]!.GetValue<int>());
        Assert.Equal(long.Parse(CuitInscripto), qr["cuit"]!.GetValue<long>());
        Assert.Equal(PuntoVenta, qr["ptoVta"]!.GetValue<int>());
        Assert.Equal(6, qr["tipoCmp"]!.GetValue<int>());
        Assert.Equal(1, qr["nroCmp"]!.GetValue<long>());
        Assert.Equal(1210m, qr["importe"]!.GetValue<decimal>());
        Assert.Equal("E", qr["tipoCodAut"]!.GetValue<string>());
        Assert.Equal(long.Parse(venta.Cae!), qr["codAut"]!.GetValue<long>());
        Assert.Null(qr["tipoDocRec"]); // consumidor final sin identificar
    }

    [Fact]
    public async Task Factura_A_a_responsable_inscripto_informa_su_CUIT_y_discrimina_IVA()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);
        var cliente = await CrearClienteAsync(a, CuitClienteInscripto, "ResponsableInscripto");

        var respuesta = await VenderAsync(a, sesion, producto, cliente.Id, "FacturaA", 1210m);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var venta = (await respuesta.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        var solicitud = Afip.SolicitudesDe(a.TenantId).Last();
        Assert.Equal((TipoComprobante.FacturaA, 80, long.Parse(CuitClienteInscripto), 1), (solicitud.Tipo, solicitud.DocTipo, solicitud.DocNro, solicitud.CondicionIvaReceptor));

        var impresion = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{venta.Id}/comprobante", ApiTestHelpers.Json))!;
        Assert.True(impresion.DiscriminaIva);
        Assert.Equal('A', impresion.Letra);
        Assert.Equal(1, impresion.CodigoAfip);
        Assert.Equal("30-71234567-1", impresion.Receptor.NumeroDocumento);
        var iva = Assert.Single(impresion.Iva);
        Assert.Equal((21m, 1000m, 210m), (iva.Porcentaje, iva.BaseImponible, iva.Importe));
        var qr = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(impresion.QrUrl!.Split("?p=")[1])))!;
        Assert.Equal(80, qr["tipoDocRec"]!.GetValue<int>());
        Assert.Equal(long.Parse(CuitClienteInscripto), qr["nroDocRec"]!.GetValue<long>());
    }

    [Fact]
    public async Task Datos_fiscales_invalidos_del_cliente_devuelven_400_sin_registrar_la_venta_ni_llamar_a_ARCA()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m, stock: 10m);
        var cuitMalVerificador = await CrearClienteAsync(a, "30712345672", "ResponsableInscripto");
        var inscripto = await CrearClienteAsync(a, CuitClienteInscripto, "ResponsableInscripto");

        var aConsumidorFinal = await VenderAsync(a, sesion, producto, null, "FacturaA", 1210m);
        var conCuitInvalido = await VenderAsync(a, sesion, producto, cuitMalVerificador.Id, "FacturaA", 1210m);
        var bAInscripto = await VenderAsync(a, sesion, producto, inscripto.Id, "FacturaB", 1210m);
        var letraDeMonotributo = await VenderAsync(a, sesion, producto, null, "FacturaC", 1210m);

        foreach (var r in new[] { aConsumidorFinal, conCuitInvalido, bAInscripto, letraDeMonotributo })
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("CUIT", await conCuitInvalido.Content.ReadAsStringAsync());

        Assert.Empty(Afip.SolicitudesDe(a.TenantId));
        var resumen = await a.ResumenCajaAsync(sesion);
        Assert.Equal(0, resumen.CantidadVentas);
        Assert.Equal(10m, (await a.Http.GetFromJsonAsync<Application.Features.Productos.ProductoDto>($"/api/productos/{producto}", ApiTestHelpers.Json))!.StockActual);
    }

    [Fact]
    public async Task Rechazo_de_ARCA_devuelve_400_con_sus_codigos_y_la_venta_queda_pendiente_de_facturacion()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m, stock: 5m);
        var noRegistrado = await CrearClienteAsync(a, AfipServiceFalso.CuitReceptorNoRegistrado, "ResponsableInscripto");

        var respuesta = await VenderAsync(a, sesion, producto, noRegistrado.Id, "FacturaA", 1210m);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = (await respuesta.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(10013, problema["erroresAfip"]![0]!["codigo"]!.GetValue<int>());
        var ventaId = problema["ventaId"]!.GetValue<Guid>();

        // La venta está firme (se entregó la mercadería) pero sin CAE
        var venta = (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{ventaId}", ApiTestHelpers.Json))!;
        Assert.Equal(EstadoFacturacion.Pendiente, venta.EstadoFacturacion);
        Assert.Null(venta.Cae);
        Assert.Equal(1, (await a.ResumenCajaAsync(sesion)).CantidadVentas);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rechazo = await db.ComprobantesFiscales.IgnoreQueryFilters().SingleAsync(c => c.VentaId == ventaId);
            Assert.Equal(ResultadoComprobanteFiscal.Rechazado, rechazo.Resultado);
            Assert.Equal(a.TenantId, rechazo.TenantId);
            Assert.Contains("10013", rechazo.ObservacionesAfip);
            Assert.Null(rechazo.Cae);
        }

        // El comprobante impreso de una venta pendiente no es fiscal
        var impresion = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{ventaId}/comprobante", ApiTestHelpers.Json))!;
        Assert.Equal("Documento no válido como factura", impresion.Leyenda);
        Assert.Null(impresion.QrUrl);
    }

    [Fact]
    public async Task Venta_pendiente_por_rechazo_se_reintenta_con_facturar_y_no_se_factura_dos_veces()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        Afip.RechazarProxima(a.TenantId, 10016, "El número de comprobante informado no es el próximo a autorizar.");
        var rechazada = await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m);
        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
        var ventaId = (await rechazada.Content.ReadFromJsonAsync<JsonObject>())!["ventaId"]!.GetValue<Guid>();

        var reintento = await a.Http.PostAsJsonAsync($"/api/ventas/{ventaId}/facturar", new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        var facturada = (await reintento.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(EstadoFacturacion.Facturada, facturada.EstadoFacturacion);
        Assert.Equal(1, facturada.NumeroComprobante); // el rechazo no consumió numeración

        var otraVez = await a.Http.PostAsJsonAsync($"/api/ventas/{ventaId}/facturar", new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
    }

    [Fact]
    public async Task Sin_comunicacion_con_ARCA_devuelve_503_la_venta_queda_pendiente_y_luego_se_factura()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        Afip.SimularCaidaProxima(a.TenantId);
        var respuesta = await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        var ventaId = (await respuesta.Content.ReadFromJsonAsync<JsonObject>())!["ventaId"]!.GetValue<Guid>();
        Assert.Equal(EstadoFacturacion.Pendiente,
            (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{ventaId}", ApiTestHelpers.Json))!.EstadoFacturacion);

        var reintento = await a.Http.PostAsJsonAsync($"/api/ventas/{ventaId}/facturar", new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
    }

    [Fact]
    public async Task Ticket_interno_se_factura_a_posteriori()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        var ticket = (await (await VenderAsync(a, sesion, producto, null, "Ticket", 1210m)).Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(EstadoFacturacion.NoRequerida, ticket.EstadoFacturacion);
        Assert.Empty(Afip.SolicitudesDe(a.TenantId));

        var comprobanteTicket = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{ticket.Id}/comprobante", ApiTestHelpers.Json))!;
        Assert.Equal((TipoComprobante.Ticket, "Documento no válido como factura"), (comprobanteTicket.Tipo, comprobanteTicket.Leyenda));
        Assert.Null(comprobanteTicket.Cae);

        var facturada = await a.Http.PostAsJsonAsync($"/api/ventas/{ticket.Id}/facturar", new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, facturada.StatusCode);
        var venta = (await facturada.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal((TipoComprobante.FacturaB, EstadoFacturacion.Facturada), (venta.TipoComprobante, venta.EstadoFacturacion));
        Assert.NotNull(venta.Cae);
    }

    [Fact]
    public async Task Monotributista_emite_factura_C_sin_discriminar_IVA_y_no_puede_emitir_B()
    {
        var a = await ComercioFacturandoAsync(CuitMonotributista, "Monotributista");
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        Assert.Equal(HttpStatusCode.BadRequest, (await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m)).StatusCode);

        var respuesta = await VenderAsync(a, sesion, producto, null, "FacturaC", 1210m);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var solicitud = Afip.SolicitudesDe(a.TenantId).Last();
        Assert.Equal((TipoComprobante.FacturaC, 1210m, 1210m, 0m), (solicitud.Tipo, solicitud.ImporteTotal, solicitud.ImporteNeto, solicitud.ImporteIva));
        Assert.Empty(solicitud.Iva);
    }

    [Fact]
    public async Task Comercio_sin_configuracion_fiscal_no_puede_facturar_422_y_no_registra_la_venta()
    {
        var a = await factory.RegistrarComercioAsync();
        var (sesion, producto) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);

        var respuesta = await VenderAsync(a, sesion, producto, null, "FacturaB", 1210m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal(0, (await a.ResumenCajaAsync(sesion)).CantidadVentas);
    }

    [Fact]
    public async Task Comercio_B_no_ve_ni_factura_ventas_del_comercio_A_y_cada_comprobante_queda_en_su_tenant()
    {
        var a = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var b = await ComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var (sesionA, productoA) = await PrepararVentaAsync(a, precio: 1000m, iva: 21m);
        var (sesionB, productoB) = await PrepararVentaAsync(b, precio: 500m, iva: 21m);

        var ventaA = (await (await VenderAsync(a, sesionA, productoA, null, "FacturaB", 1210m)).Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        var ticketA = (await (await VenderAsync(a, sesionA, productoA, null, "Ticket", 1210m)).Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        var ventaB = (await (await VenderAsync(b, sesionB, productoB, null, "FacturaB", 605m)).Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/ventas/{ventaA.Id}/comprobante")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/ventas/{ventaA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PostAsJsonAsync($"/api/ventas/{ticketA.Id}/facturar",
            new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(EstadoFacturacion.NoRequerida,
            (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{ticketA.Id}", ApiTestHelpers.Json))!.EstadoFacturacion);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var comprobantes = await db.ComprobantesFiscales.IgnoreQueryFilters()
            .Where(c => c.VentaId == ventaA.Id || c.VentaId == ventaB.Id).ToListAsync();
        Assert.Equal(a.TenantId, comprobantes.Single(c => c.VentaId == ventaA.Id).TenantId);
        Assert.Equal(b.TenantId, comprobantes.Single(c => c.VentaId == ventaB.Id).TenantId);
    }

    [Fact]
    public async Task Configuracion_fiscal_protege_el_certificado_y_es_solo_para_administradores()
    {
        var a = await factory.RegistrarComercioAsync();
        await ConfigurarDatosFiscalesAsync(a, CuitInscripto, "ResponsableInscripto");

        // Certificado de otro CUIT → 422; contraseña incorrecta → 422; CUIT con mal verificador → 400
        var deOtroCuit = await CargarCertificadoAsync(a, Certificado(CuitMonotributista, "clave-1"), "clave-1");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deOtroCuit.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CargarCertificadoAsync(a, Certificado(CuitInscripto, "clave-1"), "otra")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PutAsJsonAsync("/api/comercio/configuracion-fiscal", DatosFiscales("20111111113", "ResponsableInscripto"), ApiTestHelpers.Json)).StatusCode);

        var ok = await CargarCertificadoAsync(a, Certificado(CuitInscripto, "clave-1"), "clave-1");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var cuerpo = await (await a.Http.GetAsync("/api/comercio/configuracion-fiscal")).Content.ReadAsStringAsync();
        var config = JsonSerializer.Deserialize<ConfiguracionFiscalDto>(cuerpo, ApiTestHelpers.Json)!;
        Assert.True(config.FacturacionConfigurada);
        Assert.Contains("CUIT 20111111112", config.CertificadoSujeto);
        Assert.DoesNotContain("Protegido", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ticket", cuerpo, StringComparison.OrdinalIgnoreCase);

        // En la base el certificado está cifrado (no es el PKCS#12 original)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var comercio = await db.Comercios.SingleAsync(c => c.Id == a.TenantId);
            Assert.NotNull(comercio.CertificadoAfipProtegido);
            Assert.Throws<CryptographicException>(() =>
                X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(comercio.CertificadoAfipProtegido!), null));
        }

        // Un cajero no ve ni modifica la configuración fiscal
        using var cajero = await LoginCajeroAsync(a);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/comercio/configuracion-fiscal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PutAsJsonAsync("/api/comercio/configuracion-fiscal",
            DatosFiscales(CuitInscripto, "ResponsableInscripto"), ApiTestHelpers.Json)).StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<ComercioDePrueba> ComercioFacturandoAsync(string cuit, string condicionIva)
    {
        var comercio = await factory.RegistrarComercioAsync();
        await ConfigurarDatosFiscalesAsync(comercio, cuit, condicionIva);
        var respuesta = await CargarCertificadoAsync(comercio, Certificado(cuit, "clave-test"), "clave-test");
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        return comercio;
    }

    private static object DatosFiscales(string cuit, string condicionIva) => new
    {
        cuit,
        condicionIva,
        puntoVenta = PuntoVenta,
        domicilioFiscal = "Av. Corrientes 1234, CABA",
        ingresosBrutos = "901-123456-7",
        inicioActividades = new DateOnly(2020, 3, 1),
        produccion = false
    };

    private static async Task ConfigurarDatosFiscalesAsync(ComercioDePrueba comercio, string cuit, string condicionIva)
    {
        var respuesta = await comercio.Http.PutAsJsonAsync("/api/comercio/configuracion-fiscal", DatosFiscales(cuit, condicionIva), ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> CargarCertificadoAsync(ComercioDePrueba comercio, byte[] pkcs12, string password) =>
        comercio.Http.PutAsJsonAsync("/api/comercio/certificado-afip", new
        {
            formato = "Pkcs12",
            pkcs12Base64 = Convert.ToBase64String(pkcs12),
            password
        }, ApiTestHelpers.Json);

    /// <summary>Certificado autofirmado con el mismo formato de sujeto que emite ARCA.</summary>
    private static byte[] Certificado(string cuit, string password)
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest($"CN=posventas-test, SERIALNUMBER=CUIT {cuit}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return certificado.Export(X509ContentType.Pkcs12, password);
    }

    private static async Task<(Guid Sesion, Guid Producto)> PrepararVentaAsync(ComercioDePrueba comercio, decimal precio, decimal iva, decimal stock = 20m)
    {
        var producto = await comercio.CrearProductoOkAsync($"SKU-{Guid.NewGuid():N}"[..20], precioCompra: precio, ganancia: 0m, iva: iva, stockInicial: stock);
        var cajas = await comercio.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json);
        var sesion = cajas!.Single().SesionActiva ?? await comercio.AbrirCajaAsync(0m);
        return (sesion.Id, producto.Id);
    }

    private static Task<HttpResponseMessage> VenderAsync(
        ComercioDePrueba comercio, Guid sesionId, Guid productoId, Guid? clienteId, string tipoComprobante, decimal pago, bool? esFacturaAfip = null)
        => comercio.Http.PostAsJsonAsync("/api/ventas", new
        {
            cajaSesionId = sesionId,
            clienteId,
            tipoComprobante,
            esFacturaAfip,
            items = new[] { new { productoId, cantidad = 1m } },
            pagos = new[] { new { medio = "Efectivo", monto = pago } }
        }, ApiTestHelpers.Json);

    private static async Task<ClienteDto> CrearClienteAsync(ComercioDePrueba comercio, string documento, string condicionIva)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/clientes", new
        {
            razonSocial = $"Cliente {documento} {Guid.NewGuid():N}"[..30],
            documento,
            condicionIva,
            telefono = (string?)null,
            email = (string?)null,
            direccion = "San Martín 100",
            permiteCuentaCorriente = false,
            limiteCredito = 0m
        }, ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ClienteDto>(ApiTestHelpers.Json))!;
    }

    private async Task<HttpClient> LoginCajeroAsync(ComercioDePrueba comercio)
    {
        var alta = await comercio.Http.PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Cajero",
            nombreUsuario = "cajero1",
            email = (string?)null,
            password = "Cajero-2026-segura",
            rol = "Cajero"
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var anonimo = factory.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = comercio.Slug, username = "cajero1", password = "Cajero-2026-segura" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        return factory.CrearClienteConToken(auth.AccessToken);
    }
}
