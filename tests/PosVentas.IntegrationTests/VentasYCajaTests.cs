using System.Net;
using System.Net.Http.Json;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class VentasYCajaTests(PosVentasApiFactory factory)
{
    // Producto gravado: costo 1000 + 30% = 1300 neto, IVA 21% = 273 → 1573 por unidad
    // Producto no gravado: costo 500 + 0% = 500 neto, IVA 0%

    [Fact]
    public async Task Flujo_completo_apertura_ventas_con_varios_medios_y_cierre_con_faltante()
    {
        var a = await factory.RegistrarComercioAsync();
        var gravado = await a.CrearProductoOkAsync("GRAVADO", precioCompra: 1000, ganancia: 30, iva: 21, stockInicial: 10);
        var noGravado = await a.CrearProductoOkAsync("NO-GRAVADO", precioCompra: 500, ganancia: 0, iva: 0, stockInicial: 5);
        var sesion = await AbrirCajaAsync(a, 5000m);

        // Venta 1: 2 gravados + 1 no gravado, pagada en efectivo con vuelto
        var v1 = await VenderOkAsync(a, sesion.Id,
            [(gravado.Id, 2m), (noGravado.Id, 1m)],
            [("Efectivo", 4000m)]);

        Assert.Equal(2600m, v1.TotalNetoGravado);
        Assert.Equal(500m, v1.TotalNetoNoGravado);
        Assert.Equal(546m, v1.TotalIva);
        Assert.Equal(3646m, v1.TotalGeneral);
        Assert.Equal(354m, v1.Vuelto);
        Assert.Equal(a.Auth.UsuarioId, v1.UsuarioId);          // vendedor tomado del JWT
        Assert.Equal(sesion.Id, v1.CajaSesionId);
        Assert.Equal("Consumidor Final", v1.Cliente);
        Assert.Equal(2, v1.Detalles.Count);
        Assert.Equal(TipoComprobante.Ticket, v1.TipoComprobante);

        // El stock se descontó con los métodos de dominio
        Assert.Equal(8m, (await ObtenerProductoAsync(a, gravado.Id)).StockActual);
        Assert.Equal(4m, (await ObtenerProductoAsync(a, noGravado.Id)).StockActual);

        // Venta 2: pago combinado tarjeta + QR, sin vuelto
        var v2 = await VenderOkAsync(a, sesion.Id, [(gravado.Id, 1m)], [("Tarjeta", 1000m), ("QR", 573m)]);
        Assert.Equal(1573m, v2.TotalGeneral);
        Assert.Equal(0m, v2.Vuelto);
        Assert.Equal(2, v2.Pagos.Count);

        // Venta 3: transferencia
        await VenderOkAsync(a, sesion.Id, [(noGravado.Id, 1m)], [("Transferencia", 500m)]);

        // Vista previa del arqueo
        var resumen = await a.Http.GetFromJsonAsync<ResumenSesionDto>($"/api/cajas/sesiones/{sesion.Id}/resumen", ApiTestHelpers.Json);
        Assert.Equal(3, resumen!.CantidadVentas);
        Assert.Equal(5719m, resumen.TotalVendido);
        Assert.Equal(3646m, Total(resumen, MedioPago.Efectivo)); // 4000 recibidos - 354 de vuelto
        Assert.Equal(1000m, Total(resumen, MedioPago.Tarjeta));
        Assert.Equal(573m, Total(resumen, MedioPago.QR));
        Assert.Equal(500m, Total(resumen, MedioPago.Transferencia));
        Assert.Equal(8646m, resumen.EfectivoEsperado);            // 5000 de fondo + 3646
        Assert.True(resumen.EnCurso);

        // Cierre: se cuentan 8600 → faltan 46
        var cierre = await CerrarCajaAsync(a, sesion.Id, 8600m);
        Assert.False(cierre.EnCurso);
        Assert.Equal(8646m, cierre.EfectivoEsperado);
        Assert.Equal(8600m, cierre.MontoCierreReal);
        Assert.Equal(-46m, cierre.DiferenciaArqueo);
        Assert.Equal("Faltante", cierre.ResultadoArqueo);

        // La caja quedó cerrada y no admite más ventas
        var cajas = await a.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json);
        Assert.False(Assert.Single(cajas!).EstaAbierta);
        var fueraDeTurno = await VenderAsync(a, sesion.Id, [(gravado.Id, 1m)], [("Efectivo", 2000m)]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fueraDeTurno.StatusCode);
    }

    [Theory]
    [InlineData(1573, 0, "Exacto")]
    [InlineData(1600, 27, "Sobrante")]
    public async Task Cierre_calcula_exacto_y_sobrante(decimal contado, decimal diferenciaEsperada, string resultadoEsperado)
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("ARQUEO", stockInicial: 3);
        var sesion = await AbrirCajaAsync(a, 0m);
        await VenderOkAsync(a, sesion.Id, [(producto.Id, 1m)], [("Efectivo", 1573m)]);

        var cierre = await CerrarCajaAsync(a, sesion.Id, contado);

        Assert.Equal(diferenciaEsperada, cierre.DiferenciaArqueo);
        Assert.Equal(resultadoEsperado, cierre.ResultadoArqueo);
    }

    [Fact]
    public async Task Stock_insuficiente_devuelve_422_y_no_persiste_nada()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("POCO-STOCK", stockInicial: 2);
        var sesion = await AbrirCajaAsync(a, 0m);

        var respuesta = await VenderAsync(a, sesion.Id, [(producto.Id, 3m)], [("Efectivo", 10000m)]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal(2m, (await ObtenerProductoAsync(a, producto.Id)).StockActual);
        var resumen = await a.Http.GetFromJsonAsync<ResumenSesionDto>($"/api/cajas/sesiones/{sesion.Id}/resumen", ApiTestHelpers.Json);
        Assert.Equal(0, resumen!.CantidadVentas);
    }

    [Theory]
    [InlineData("Efectivo", 1000)]   // no cubre el total (1573)
    [InlineData("Tarjeta", 2000)]    // vuelto con tarjeta: no permitido
    public async Task Pagos_invalidos_devuelven_422(string medio, decimal monto)
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("PAGO", stockInicial: 5);
        var sesion = await AbrirCajaAsync(a, 0m);

        var respuesta = await VenderAsync(a, sesion.Id, [(producto.Id, 1m)], [(medio, monto)]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal(5m, (await ObtenerProductoAsync(a, producto.Id)).StockActual);
    }

    [Fact]
    public async Task Venta_con_datos_invalidos_devuelve_400()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("VALIDA", stockInicial: 5);
        var sesion = await AbrirCajaAsync(a, 0m);

        var sinItems = await VenderAsync(a, sesion.Id, [], [("Efectivo", 100m)]);
        var cantidadNegativa = await VenderAsync(a, sesion.Id, [(producto.Id, -1m)], [("Efectivo", 100m)]);
        var sinPagos = await VenderAsync(a, sesion.Id, [(producto.Id, 1m)], []);
        // Una nota de crédito no se emite como venta (se genera desde la factura que anula)
        var factura = await VenderAsync(a, sesion.Id, [(producto.Id, 1m)], [("Efectivo", 2000m)], tipoComprobante: "NotaCreditoA");

        Assert.Equal(HttpStatusCode.BadRequest, sinItems.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cantidadNegativa.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinPagos.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, factura.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_abrir_dos_veces_la_misma_caja()
    {
        var a = await factory.RegistrarComercioAsync();
        var caja = Assert.Single((await a.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json))!);

        var primera = await a.Http.PostAsJsonAsync($"/api/cajas/{caja.Id}/apertura", new { montoApertura = 100 });
        var segunda = await a.Http.PostAsJsonAsync($"/api/cajas/{caja.Id}/apertura", new { montoApertura = 100 });

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);
    }

    [Fact]
    public async Task Tenant_B_no_puede_vender_productos_ni_usar_cajas_ni_ver_ventas_del_Tenant_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var productoA = await a.CrearProductoOkAsync("SOLO-A", stockInicial: 5);
        var productoB = await b.CrearProductoOkAsync("SOLO-B", stockInicial: 5);
        var sesionA = await AbrirCajaAsync(a, 0m);
        var sesionB = await AbrirCajaAsync(b, 0m);
        var ventaA = await VenderOkAsync(a, sesionA.Id, [(productoA.Id, 1m)], [("Efectivo", 1573m)]);
        var cajaA = Assert.Single((await a.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json))!);

        // Producto de A en la caja de B
        Assert.Equal(HttpStatusCode.NotFound, (await VenderAsync(b, sesionB.Id, [(productoA.Id, 1m)], [("Efectivo", 2000m)])).StatusCode);
        // Producto de B en la caja de A (usando el token de B)
        Assert.Equal(HttpStatusCode.NotFound, (await VenderAsync(b, sesionA.Id, [(productoB.Id, 1m)], [("Efectivo", 2000m)])).StatusCode);
        // Venta, resumen, cierre y apertura sobre recursos de A
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/ventas/{ventaA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/cajas/sesiones/{sesionA.Id}/resumen")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesionA.Id}/cierre", new { montoCierreReal = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PostAsJsonAsync($"/api/cajas/{cajaA.Id}/apertura", new { montoApertura = 0 })).StatusCode);

        // A sigue intacto
        Assert.Equal(4m, (await ObtenerProductoAsync(a, productoA.Id)).StockActual);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.GetAsync($"/api/ventas/{ventaA.Id}")).StatusCode);
    }

    // ---------- helpers ----------

    private static decimal Total(ResumenSesionDto resumen, MedioPago medio) =>
        resumen.TotalesPorMedio.Single(t => t.Medio == medio).Total;

    private static async Task<SesionCajaDto> AbrirCajaAsync(ComercioDePrueba comercio, decimal monto)
    {
        var caja = Assert.Single((await comercio.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json))!);
        var respuesta = await comercio.Http.PostAsJsonAsync($"/api/cajas/{caja.Id}/apertura", new { montoApertura = monto });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<SesionCajaDto>(ApiTestHelpers.Json))!;
    }

    private static async Task<ResumenSesionDto> CerrarCajaAsync(ComercioDePrueba comercio, Guid sesionId, decimal contado)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesionId}/cierre",
            new { montoCierreReal = contado, observaciones = "cierre de prueba" });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ResumenSesionDto>(ApiTestHelpers.Json))!;
    }

    private static Task<HttpResponseMessage> VenderAsync(
        ComercioDePrueba comercio, Guid sesionId,
        (Guid ProductoId, decimal Cantidad)[] items, (string Medio, decimal Monto)[] pagos,
        string tipoComprobante = "Ticket")
        => comercio.Http.PostAsJsonAsync("/api/ventas", new
        {
            cajaSesionId = sesionId,
            clienteId = (Guid?)null,
            tipoComprobante,
            items = items.Select(i => new { productoId = i.ProductoId, cantidad = i.Cantidad }),
            pagos = pagos.Select(p => new { medio = p.Medio, monto = p.Monto })
        });

    private static async Task<VentaDto> VenderOkAsync(
        ComercioDePrueba comercio, Guid sesionId,
        (Guid ProductoId, decimal Cantidad)[] items, (string Medio, decimal Monto)[] pagos)
    {
        var respuesta = await VenderAsync(comercio, sesionId, items, pagos);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
    }

    private static async Task<ProductoDto> ObtenerProductoAsync(ComercioDePrueba comercio, Guid id) =>
        (await comercio.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{id}", ApiTestHelpers.Json))!;
}
