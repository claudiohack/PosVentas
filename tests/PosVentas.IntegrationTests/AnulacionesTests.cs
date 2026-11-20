using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Application.Features.Anulaciones;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Reportes;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;
using PosVentas.Infrastructure.Persistence;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class AnulacionesTests(PosVentasApiFactory factory)
{
    private const string CuitInscripto = "20111111112";
    private const string CuitMonotributista = "27222222228";
    private const string Motivo = "El cliente devolvió la mercadería";

    private AfipServiceFalso Afip => factory.Services.GetRequiredService<AfipServiceFalso>();

    // a) Anulación atómica
    [Fact]
    public async Task Anular_reintegra_stock_devuelve_el_dinero_en_caja_y_restituye_el_credito_del_cliente()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("ANUL-1", precioCompra: 1000m, ganancia: 0m, iva: 0m, stockInicial: 10m);
        var cliente = await a.CrearClienteFiscalAsync("30712345671", "ResponsableInscripto", limiteCredito: 5000m);
        var sesion = await a.AbrirCajaAsync(2000m);

        // 3 × 1000 = 3000: 1000 en efectivo + 500 con tarjeta + 1500 a cuenta corriente
        var venta = await VenderOkAsync(a, sesion.Id, cliente.Id, "Ticket", (producto.Id, 3m),
            ("Efectivo", 1000m), ("Tarjeta", 500m), ("CuentaCorriente", 1500m));
        Assert.Equal(7m, (await ProductoAsync(a, producto.Id)).StockActual);
        Assert.Equal(1500m, (await a.CuentaCorrienteAsync(cliente.Id)).Cliente.SaldoActual);
        Assert.Equal(3000m, (await a.ResumenCajaAsync(sesion.Id)).EfectivoEsperado);

        var respuesta = await AnularAsync(a, venta.Id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var anulacion = (await respuesta.Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;
        Assert.Equal((3000m, 1500m, 1500m, sesion.Id, EstadoNotaCredito.NoRequerida),
            (anulacion.MontoTotalRevertido, anulacion.MontoDevueltoEnCaja, anulacion.MontoAcreditadoCuentaCorriente, anulacion.CajaSesionId, anulacion.EstadoNotaCredito));
        Assert.Equal(Motivo, anulacion.Motivo);
        Assert.Equal("Admin de prueba", anulacion.Usuario);

        // Stock reintegrado
        Assert.Equal(10m, (await ProductoAsync(a, producto.Id)).StockActual);

        // Egresos en la caja: el efectivo esperado vuelve al fondo inicial; la devolución con tarjeta queda registrada
        var resumen = await a.ResumenCajaAsync(sesion.Id);
        Assert.Equal(2000m, resumen.EfectivoEsperado);
        Assert.Equal((1, 1500m), (resumen.CantidadDevoluciones, resumen.TotalDevoluciones));

        // Cupo de crédito restituido, con su asiento en la cuenta corriente
        var cuenta = await a.CuentaCorrienteAsync(cliente.Id);
        Assert.Equal((0m, 5000m), (cuenta.Cliente.SaldoActual, cuenta.Cliente.CreditoDisponible));
        var ajuste = cuenta.Movimientos.First();
        Assert.Equal((TipoMovimientoCuentaCorriente.Abono, 1500m, venta.Id), (ajuste.Tipo, ajuste.Monto, ajuste.VentaId));

        // La venta queda anulada y sale de las métricas de venta
        Assert.Equal(EstadoVenta.Anulada, (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{venta.Id}", ApiTestHelpers.Json))!.Estado);
        var dashboard = (await a.Http.GetFromJsonAsync<DashboardResumenDto>("/api/reportes/dashboard", ApiTestHelpers.Json))!;
        Assert.Equal((0, 0m, 1, 3000m), (dashboard.CantidadVentas, dashboard.TotalVendido, dashboard.CantidadVentasAnuladas, dashboard.TotalAnulado));

        // Anular dos veces → 409
        Assert.Equal(HttpStatusCode.Conflict, (await AnularAsync(a, venta.Id)).StatusCode);
        Assert.Equal(10m, (await ProductoAsync(a, producto.Id)).StockActual);
    }

    [Fact]
    public async Task El_efectivo_se_devuelve_neto_del_vuelto_y_la_anulacion_sin_fondos_no_modifica_nada()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("ANUL-2", precioCompra: 1000m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(0m);

        // Paga 1200 por 1000: el vuelto de 200 ya se entregó, se devuelven 1000
        var venta = await VenderOkAsync(a, sesion.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 1200m));
        var anulacion = (await (await AnularAsync(a, venta.Id)).Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;
        Assert.Equal(1000m, anulacion.MontoDevueltoEnCaja);
        Assert.Equal(0m, (await a.ResumenCajaAsync(sesion.Id)).EfectivoEsperado);

        // Otra venta; se paga un proveedor con todo el efectivo: no alcanza para devolver → 422 y nada cambia
        var otra = await VenderOkAsync(a, sesion.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 1000m));
        await PagarProveedorEnEfectivoAsync(a, producto.Id, 1000m);
        var sinFondos = await AnularAsync(a, otra.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinFondos.StatusCode);
        Assert.Equal(EstadoVenta.Activa, (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{otra.Id}", ApiTestHelpers.Json))!.Estado);
        Assert.Equal(5m, (await ProductoAsync(a, producto.Id)).StockActual); // 5 − 1 vendido + 1 comprado
    }

    [Fact]
    public async Task Una_venta_de_un_turno_cerrado_se_devuelve_desde_la_caja_abierta_y_sin_caja_abierta_es_422()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("ANUL-3", precioCompra: 500m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var turno1 = await a.AbrirCajaAsync(0m);
        var venta = await VenderOkAsync(a, turno1.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 500m));
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PostAsJsonAsync($"/api/cajas/sesiones/{turno1.Id}/cierre", new { montoCierreReal = 500m })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AnularAsync(a, venta.Id)).StatusCode);

        var turno2 = await a.AbrirCajaAsync(1000m);
        var anulacion = (await (await AnularAsync(a, venta.Id)).Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;
        Assert.Equal(turno2.Id, anulacion.CajaSesionId);
        Assert.Equal(500m, (await a.ResumenCajaAsync(turno2.Id)).EfectivoEsperado);
    }

    // b) Nota de crédito ARCA
    [Fact]
    public async Task Anular_una_factura_B_emite_la_nota_de_credito_B_asociada_con_CAE_y_QR()
    {
        var a = await factory.RegistrarComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var producto = await a.CrearProductoOkAsync("NC-B", precioCompra: 1000m, ganancia: 0m, iva: 21m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(5000m);
        var venta = await VenderOkAsync(a, sesion.Id, null, "FacturaB", (producto.Id, 1m), ("Efectivo", 1210m));
        Assert.Equal(EstadoFacturacion.Facturada, venta.EstadoFacturacion);

        var respuesta = await AnularAsync(a, venta.Id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var anulacion = (await respuesta.Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;
        Assert.Equal((EstadoNotaCredito.Emitida, TipoComprobante.NotaCreditoB, "00003-00000001"),
            (anulacion.EstadoNotaCredito, anulacion.NotaCreditoTipo, anulacion.NotaCreditoNumero));
        Assert.Matches("^[0-9]{14}$", anulacion.NotaCreditoCae);

        // Lo que se le pidió a ARCA: NC B con la factura como comprobante asociado y los mismos importes y receptor
        var solicitud = Afip.SolicitudesDe(a.TenantId).Last();
        Assert.Equal(TipoComprobante.NotaCreditoB, solicitud.Tipo);
        Assert.Equal((TipoComprobante.FacturaB, FiscalTestHelpers.PuntoVenta, 1L, CuitInscripto),
            (solicitud.Asociado!.Tipo, solicitud.Asociado.PuntoVenta, solicitud.Asociado.Numero, solicitud.Asociado.CuitEmisor));
        Assert.Equal((1210m, 1000m, 210m, 99), (solicitud.ImporteTotal, solicitud.ImporteNeto, solicitud.ImporteIva, solicitud.DocTipo));

        // ComprobanteFiscal de la NC, en el tenant A, asociado a la factura original
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var comprobantes = await db.ComprobantesFiscales.IgnoreQueryFilters().Where(c => c.VentaId == venta.Id).ToListAsync();
            var factura = comprobantes.Single(c => c.TipoComprobante == TipoComprobante.FacturaB);
            var nota = comprobantes.Single(c => c.TipoComprobante == TipoComprobante.NotaCreditoB);
            Assert.Equal(factura.Id, nota.ComprobanteAsociadoId);
            Assert.Equal((ResultadoComprobanteFiscal.Aprobado, a.TenantId, anulacion.NotaCreditoCae), (nota.Resultado, nota.TenantId, nota.Cae));
        }

        // Comprobante imprimible de la NC con su QR (tipoCmp 8)
        var impresion = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{venta.Id}/nota-credito", ApiTestHelpers.Json))!;
        Assert.Equal(("Nota de crédito B", 8, "Factura B 00003-00000001"), (impresion.Descripcion, impresion.CodigoAfip, impresion.ComprobanteAsociado));
        var qr = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(impresion.QrUrl!.Split("?p=")[1])))!;
        Assert.Equal((8, long.Parse(anulacion.NotaCreditoCae!)), (qr["tipoCmp"]!.GetValue<int>(), qr["codAut"]!.GetValue<long>()));

        // La factura original sigue imprimiéndose, marcada como anulada
        var original = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{venta.Id}/comprobante", ApiTestHelpers.Json))!;
        Assert.Equal(("Factura B", true), (original.Descripcion, original.Anulada));
    }

    [Fact]
    public async Task Monotributista_anula_factura_C_con_nota_de_credito_C_sin_IVA()
    {
        var a = await factory.RegistrarComercioFacturandoAsync(CuitMonotributista, "Monotributista");
        var producto = await a.CrearProductoOkAsync("NC-C", precioCompra: 800m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(0m);
        var venta = await VenderOkAsync(a, sesion.Id, null, "FacturaC", (producto.Id, 1m), ("Transferencia", 800m));

        var anulacion = (await (await AnularAsync(a, venta.Id)).Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;

        Assert.Equal((EstadoNotaCredito.Emitida, TipoComprobante.NotaCreditoC), (anulacion.EstadoNotaCredito, anulacion.NotaCreditoTipo));
        var solicitud = Afip.SolicitudesDe(a.TenantId).Last();
        Assert.Equal((TipoComprobante.NotaCreditoC, 800m, 0m, TipoComprobante.FacturaC), (solicitud.Tipo, solicitud.ImporteNeto, solicitud.ImporteIva, solicitud.Asociado!.Tipo));
        Assert.Empty(solicitud.Iva);
    }

    [Fact]
    public async Task Si_ARCA_rechaza_la_nota_de_credito_la_anulacion_queda_hecha_y_la_NC_pendiente_hasta_reintentar()
    {
        var a = await factory.RegistrarComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var producto = await a.CrearProductoOkAsync("NC-R", precioCompra: 1000m, ganancia: 0m, iva: 21m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(5000m);
        var venta = await VenderOkAsync(a, sesion.Id, null, "FacturaB", (producto.Id, 1m), ("Efectivo", 1210m));

        Afip.RechazarProxima(a.TenantId, 10153, "El comprobante asociado no se encuentra autorizado.");
        var rechazada = await AnularAsync(a, venta.Id);

        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
        var problema = (await rechazada.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(10153, problema["erroresAfip"]![0]!["codigo"]!.GetValue<int>());
        Assert.Contains("quedó anulada", problema["detail"]!.GetValue<string>());

        // Anulación firme: stock y caja revertidos; NC pendiente
        Assert.Equal(5m, (await ProductoAsync(a, producto.Id)).StockActual);
        var pendiente = (await a.Http.GetFromJsonAsync<AnulacionDto>($"/api/ventas/{venta.Id}/anulacion", ApiTestHelpers.Json))!;
        Assert.Equal(EstadoNotaCredito.Pendiente, pendiente.EstadoNotaCredito);
        var interno = (await a.Http.GetFromJsonAsync<ComprobanteImpresionDto>($"/api/ventas/{venta.Id}/nota-credito", ApiTestHelpers.Json))!;
        Assert.Null(interno.Cae);
        Assert.Equal("Nota de crédito pendiente de autorización por ARCA", interno.Leyenda);

        // Caída de ARCA en el primer reintento → 503; el segundo la emite
        Afip.SimularCaidaProxima(a.TenantId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await a.Http.PostAsync($"/api/ventas/{venta.Id}/nota-credito", null)).StatusCode);
        var reintento = await a.Http.PostAsync($"/api/ventas/{venta.Id}/nota-credito", null);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        var emitida = (await reintento.Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;
        Assert.Equal((EstadoNotaCredito.Emitida, "00003-00000001"), (emitida.EstadoNotaCredito, emitida.NotaCreditoNumero));
        Assert.Equal(HttpStatusCode.Conflict, (await a.Http.PostAsync($"/api/ventas/{venta.Id}/nota-credito", null)).StatusCode);
    }

    [Fact]
    public async Task Una_venta_con_factura_pendiente_se_anula_sin_nota_de_credito_y_ya_no_se_puede_facturar()
    {
        var a = await factory.RegistrarComercioFacturandoAsync(CuitInscripto, "ResponsableInscripto");
        var producto = await a.CrearProductoOkAsync("NC-P", precioCompra: 1000m, ganancia: 0m, iva: 21m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(5000m);

        Afip.RechazarProxima(a.TenantId, 10016, "Número no correlativo.");
        var rechazo = await VenderAsync(a, sesion.Id, null, "FacturaB", (producto.Id, 1m), ("Efectivo", 1210m));
        var ventaId = (await rechazo.Content.ReadFromJsonAsync<JsonObject>())!["ventaId"]!.GetValue<Guid>();
        var solicitudesAntes = Afip.SolicitudesDe(a.TenantId).Count;

        var anulacion = (await (await AnularAsync(a, ventaId)).Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!;

        Assert.Equal(EstadoNotaCredito.NoRequerida, anulacion.EstadoNotaCredito);
        Assert.Equal(solicitudesAntes, Afip.SolicitudesDe(a.TenantId).Count); // no se llamó a ARCA
        var facturar = await a.Http.PostAsJsonAsync($"/api/ventas/{ventaId}/facturar", new { tipoComprobante = "FacturaB" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, facturar.StatusCode);
    }

    // c) RBAC
    [Fact]
    public async Task Cajero_recibe_403_al_anular_y_un_supervisor_puede_anular()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("RBAC-A", precioCompra: 100m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(1000m);
        var venta = await VenderOkAsync(a, sesion.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 100m));

        using var cajero = await factory.LoginConRolAsync(a, "cajero1", "Cajero");
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PostAsJsonAsync($"/api/ventas/{venta.Id}/anular", new { motivoAnulacion = Motivo })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PostAsync($"/api/ventas/{venta.Id}/nota-credito", null)).StatusCode);
        Assert.Equal(EstadoVenta.Activa, (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{venta.Id}", ApiTestHelpers.Json))!.Estado);

        using var supervisor = await factory.LoginConRolAsync(a, "super1", "Supervisor");
        var anulada = await supervisor.PostAsJsonAsync($"/api/ventas/{venta.Id}/anular", new { motivoAnulacion = Motivo });
        Assert.Equal(HttpStatusCode.OK, anulada.StatusCode);
        Assert.Equal("super1", (await anulada.Content.ReadFromJsonAsync<AnulacionDto>(ApiTestHelpers.Json))!.Usuario);

        // Motivo obligatorio
        var otra = await VenderOkAsync(a, sesion.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 100m));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PostAsJsonAsync($"/api/ventas/{otra.Id}/anular", new { motivoAnulacion = "" })).StatusCode);
    }

    // d) Aislamiento multi-tenant
    [Fact]
    public async Task Comercio_B_recibe_404_al_anular_o_consultar_la_anulacion_de_una_venta_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("ISO-AN", precioCompra: 100m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesionA = await a.AbrirCajaAsync(1000m);
        var venta = await VenderOkAsync(a, sesionA.Id, null, "Ticket", (producto.Id, 1m), ("Efectivo", 100m));
        await b.AbrirCajaAsync(1000m);

        Assert.Equal(HttpStatusCode.NotFound, (await AnularAsync(b, venta.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PostAsync($"/api/ventas/{venta.Id}/nota-credito", null)).StatusCode);

        // Ni siquiera indicando la caja de A puede devolver dinero desde ella
        var conCajaDeA = await b.Http.PostAsJsonAsync($"/api/ventas/{venta.Id}/anular", new { motivoAnulacion = Motivo, cajaSesionId = sesionA.Id });
        Assert.Equal(HttpStatusCode.NotFound, conCajaDeA.StatusCode);

        Assert.Equal(EstadoVenta.Activa, (await a.Http.GetFromJsonAsync<VentaDto>($"/api/ventas/{venta.Id}", ApiTestHelpers.Json))!.Estado);
        Assert.Equal(4m, (await ProductoAsync(a, producto.Id)).StockActual);

        // Una vez anulada por A, B tampoco ve la anulación ni la nota
        Assert.Equal(HttpStatusCode.OK, (await AnularAsync(a, venta.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/ventas/{venta.Id}/anulacion")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/ventas/{venta.Id}/nota-credito")).StatusCode);
        Assert.Empty((await b.Http.GetFromJsonAsync<List<VentaHistorialDto>>("/api/ventas", ApiTestHelpers.Json))!);
        var historialA = (await a.Http.GetFromJsonAsync<List<VentaHistorialDto>>("/api/ventas?estado=Anulada", ApiTestHelpers.Json))!;
        Assert.Equal((venta.Id, EstadoVenta.Anulada, EstadoNotaCredito.NoRequerida), (historialA.Single().Id, historialA.Single().Estado, historialA.Single().EstadoNotaCredito));
    }

    // ---------- Helpers ----------

    private static Task<HttpResponseMessage> AnularAsync(ComercioDePrueba comercio, Guid ventaId) =>
        comercio.Http.PostAsJsonAsync($"/api/ventas/{ventaId}/anular", new { motivoAnulacion = Motivo }, ApiTestHelpers.Json);

    private static Task<HttpResponseMessage> VenderAsync(
        ComercioDePrueba comercio, Guid sesionId, Guid? clienteId, string tipoComprobante,
        (Guid ProductoId, decimal Cantidad) item, params (string Medio, decimal Monto)[] pagos)
        => comercio.Http.PostAsJsonAsync("/api/ventas", new
        {
            cajaSesionId = sesionId,
            clienteId,
            tipoComprobante,
            items = new[] { new { productoId = item.ProductoId, cantidad = item.Cantidad } },
            pagos = pagos.Select(p => new { medio = p.Medio, monto = p.Monto })
        }, ApiTestHelpers.Json);

    private static async Task<VentaDto> VenderOkAsync(
        ComercioDePrueba comercio, Guid sesionId, Guid? clienteId, string tipoComprobante,
        (Guid ProductoId, decimal Cantidad) item, params (string Medio, decimal Monto)[] pagos)
    {
        var respuesta = await VenderAsync(comercio, sesionId, clienteId, tipoComprobante, item, pagos);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
    }

    private static async Task<ProductoDto> ProductoAsync(ComercioDePrueba comercio, Guid id) =>
        (await comercio.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{id}", ApiTestHelpers.Json))!;

    private static async Task PagarProveedorEnEfectivoAsync(ComercioDePrueba comercio, Guid productoId, decimal monto)
    {
        var cuit = Random.Shared.NextInt64(20_000_000_000, 34_999_999_999).ToString();
        var alta = await comercio.Http.PostAsJsonAsync("/api/proveedores", new { razonSocial = $"Prov {cuit}", cuit, telefono = (string?)null, email = (string?)null, direccion = (string?)null });
        var proveedorId = (await alta.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        var compra = await comercio.Http.PostAsJsonAsync("/api/compras", new
        {
            proveedorId, numeroComprobante = $"R-{Guid.NewGuid():N}"[..12], fechaCompra = DateOnly.FromDateTime(DateTime.UtcNow),
            formaPago = "Efectivo", cajaSesionId = (Guid?)null, observaciones = (string?)null,
            items = new[] { new { productoId, cantidad = 1m, costoUnitario = monto } }
        }, ApiTestHelpers.Json);
        Assert.True(compra.StatusCode == HttpStatusCode.Created, await compra.Content.ReadAsStringAsync());
    }
}
