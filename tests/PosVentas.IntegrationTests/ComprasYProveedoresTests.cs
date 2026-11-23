using System.Net;
using System.Net.Http.Json;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Compras;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Proveedores;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class ComprasYProveedoresTests(PosVentasApiFactory factory)
{
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    // a) Ingreso de stock atómico
    [Fact]
    public async Task Registrar_compra_incrementa_stock_actualiza_costo_y_genera_detalles()
    {
        var a = await factory.RegistrarComercioAsync();
        var proveedor = await CrearProveedorAsync(a);
        var producto = await a.CrearProductoOkAsync("SKU-CMP", precioCompra: 1000m, ganancia: 30m, iva: 21m, stockInicial: 10m);

        var respuesta = await RegistrarCompraAsync(a, proveedor.Id, "FC-A-0001-00001234", "CuentaCorrienteProveedor", null,
            (producto.Id, 10m, 1200m));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var compra = (await respuesta.Content.ReadFromJsonAsync<CompraDto>(ApiTestHelpers.Json))!;
        Assert.Equal(12_000m, compra.MontoTotal);
        Assert.Null(compra.CajaSesionId);
        var detalle = Assert.Single(compra.Detalles);
        Assert.Equal((producto.Id, 10m, 1200m, 12_000m), (detalle.ProductoId, detalle.Cantidad, detalle.CostoUnitario, detalle.Subtotal));

        // Stock 10 + 10; costo promedio ponderado (10×1000 + 10×1200) / 20 = 1100; venta = 1100 × 1,30
        var actualizado = await ObtenerProductoAsync(a, producto.Id);
        Assert.Equal(20m, actualizado.StockActual);
        Assert.Equal(1100m, actualizado.PrecioCompra);
        Assert.Equal(1430m, actualizado.PrecioVenta);

        var persistida = await a.Http.GetFromJsonAsync<CompraDto>($"/api/compras/{compra.Id}", ApiTestHelpers.Json);
        Assert.Single(persistida!.Detalles);
        var listado = await a.Http.GetFromJsonAsync<List<CompraResumenDto>>("/api/compras", ApiTestHelpers.Json);
        Assert.Contains(listado!, c => c.Id == compra.Id && c.CantidadItems == 1 && !c.ImpactoEnCaja);
    }

    [Fact]
    public async Task Compra_rechazada_no_modifica_ningun_stock()
    {
        var a = await factory.RegistrarComercioAsync();
        var proveedor = await CrearProveedorAsync(a);
        var producto = await a.CrearProductoOkAsync("SKU-ATOM", precioCompra: 500m, stockInicial: 4m);

        // Un renglón válido y otro con un producto inexistente: no debe ingresar nada
        var conInexistente = await RegistrarCompraAsync(a, proveedor.Id, "R-0001", "CuentaCorrienteProveedor", null,
            (producto.Id, 6m, 500m), (Guid.NewGuid(), 1m, 100m));
        Assert.Equal(HttpStatusCode.NotFound, conInexistente.StatusCode);

        // El mismo comprobante del mismo proveedor no se carga dos veces
        Assert.Equal(HttpStatusCode.Created, (await RegistrarCompraAsync(a, proveedor.Id, "R-0002", "CuentaCorrienteProveedor", null,
            (producto.Id, 1m, 500m))).StatusCode);
        var repetida = await RegistrarCompraAsync(a, proveedor.Id, "r-0002", "CuentaCorrienteProveedor", null, (producto.Id, 1m, 500m));
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);

        var sinItems = await RegistrarCompraAsync(a, proveedor.Id, "R-0003", "CuentaCorrienteProveedor", null);
        Assert.Equal(HttpStatusCode.BadRequest, sinItems.StatusCode);

        Assert.Equal(5m, (await ObtenerProductoAsync(a, producto.Id)).StockActual); // 4 + 1 de la única compra válida
    }

    // b) Impacto en caja
    [Fact]
    public async Task Compra_en_efectivo_descuenta_el_efectivo_esperado_de_la_caja_activa()
    {
        var a = await factory.RegistrarComercioAsync();
        var proveedor = await CrearProveedorAsync(a);
        var producto = await a.CrearProductoOkAsync("SKU-CAJA", precioCompra: 1000m);
        var sesion = await a.AbrirCajaAsync(10_000m);

        // Sin indicar caja: toma la única abierta
        var enEfectivo = await RegistrarCompraAsync(a, proveedor.Id, "X-1", "Efectivo", null, (producto.Id, 3m, 1000m));
        Assert.Equal(HttpStatusCode.Created, enEfectivo.StatusCode);
        Assert.Equal(sesion.Id, (await enEfectivo.Content.ReadFromJsonAsync<CompraDto>(ApiTestHelpers.Json))!.CajaSesionId);

        var resumen = await a.ResumenCajaAsync(sesion.Id);
        Assert.Equal(7_000m, resumen.EfectivoEsperado);
        Assert.Equal(1, resumen.CantidadPagosProveedores);
        Assert.Equal(3_000m, resumen.TotalPagosProveedores);

        // Transferencia: queda registrada en el turno pero no toca el efectivo
        var porTransferencia = await RegistrarCompraAsync(a, proveedor.Id, "X-2", "Transferencia", sesion.Id, (producto.Id, 1m, 500m));
        Assert.Equal(HttpStatusCode.Created, porTransferencia.StatusCode);
        resumen = await a.ResumenCajaAsync(sesion.Id);
        Assert.Equal(7_000m, resumen.EfectivoEsperado);
        Assert.Equal(3_500m, resumen.TotalPagosProveedores);

        // No se puede pagar en efectivo más de lo que hay en la caja (y no ingresa stock)
        var sinFondos = await RegistrarCompraAsync(a, proveedor.Id, "X-3", "Efectivo", sesion.Id, (producto.Id, 8m, 1000m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinFondos.StatusCode);
        Assert.Equal(4m, (await ObtenerProductoAsync(a, producto.Id)).StockActual);

        // El arqueo del cierre contempla el egreso
        var cierre = await a.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesion.Id}/cierre", new { montoCierreReal = 7_000m });
        var resultado = await cierre.Content.ReadFromJsonAsync<Application.Features.Cajas.ResumenSesionDto>(ApiTestHelpers.Json);
        Assert.Equal("Exacto", resultado!.ResultadoArqueo);

        // A cuenta corriente con la caja cerrada: se registra sin impacto en caja
        var aCuenta = await RegistrarCompraAsync(a, proveedor.Id, "X-4", "CuentaCorrienteProveedor", null, (producto.Id, 1m, 1000m));
        Assert.Equal(HttpStatusCode.Created, aCuenta.StatusCode);
    }

    // c) Stock bajo
    [Fact]
    public async Task Stock_bajo_devuelve_solo_productos_con_existencias_menores_o_iguales_al_minimo()
    {
        var a = await factory.RegistrarComercioAsync();
        var debajo = await CrearConStockAsync(a, "SKU-DEBAJO", stock: 2m, minimo: 5m);
        var igual = await CrearConStockAsync(a, "SKU-IGUAL", stock: 5m, minimo: 5m);
        var arriba = await CrearConStockAsync(a, "SKU-ARRIBA", stock: 10m, minimo: 5m);
        var sinMinimoConStock = await CrearConStockAsync(a, "SKU-SINMIN", stock: 3m, minimo: 0m);

        var stockBajo = (await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos/stock-bajo", ApiTestHelpers.Json))!;

        // Los más críticos primero (mayor faltante respecto del mínimo)
        Assert.Equal([debajo.Id, igual.Id], stockBajo.Select(p => p.Id).ToList());
        Assert.All(stockBajo, p => Assert.True(p.StockActual <= p.StockMinimo && p.StockBajo));
        Assert.DoesNotContain(stockBajo, p => p.Id == arriba.Id || p.Id == sinMinimoConStock.Id);

        // Una compra que repone por encima del mínimo saca al producto de la alerta
        var proveedor = await CrearProveedorAsync(a);
        await RegistrarCompraAsync(a, proveedor.Id, "REP-1", "CuentaCorrienteProveedor", null, (debajo.Id, 10m, 1000m));
        stockBajo = (await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos/stock-bajo", ApiTestHelpers.Json))!;
        Assert.Equal([igual.Id], stockBajo.Select(p => p.Id).ToList());
    }

    // d) Aislamiento multi-tenant
    [Fact]
    public async Task Comercio_B_recibe_404_al_ver_o_usar_proveedores_y_compras_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var proveedorA = await CrearProveedorAsync(a);
        var productoA = await a.CrearProductoOkAsync("SKU-ISO", precioCompra: 100m, stockInicial: 1m);
        var compraA = (await (await RegistrarCompraAsync(a, proveedorA.Id, "ISO-1", "CuentaCorrienteProveedor", null,
            (productoA.Id, 1m, 100m))).Content.ReadFromJsonAsync<CompraDto>(ApiTestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/proveedores/{proveedorA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"/api/proveedores/{proveedorA.Id}",
            new { razonSocial = "Robado", cuit = proveedorA.Cuit, telefono = (string?)null, email = (string?)null, direccion = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PatchAsJsonAsync($"/api/proveedores/{proveedorA.Id}/estado", new { activo = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/compras/{compraA.Id}")).StatusCode);

        // B no puede comprarle a un proveedor de A ni ingresar stock en un producto de A
        Assert.Equal(HttpStatusCode.NotFound, (await RegistrarCompraAsync(b, proveedorA.Id, "ISO-B1", "CuentaCorrienteProveedor", null,
            (productoA.Id, 5m, 100m))).StatusCode);
        var proveedorB = await CrearProveedorAsync(b, proveedorA.Cuit); // mismo CUIT en otro comercio: permitido
        Assert.Equal(HttpStatusCode.NotFound, (await RegistrarCompraAsync(b, proveedorB.Id, "ISO-B2", "CuentaCorrienteProveedor", null,
            (productoA.Id, 5m, 100m))).StatusCode);

        Assert.DoesNotContain((await b.Http.GetFromJsonAsync<List<ProveedorDto>>("/api/proveedores", ApiTestHelpers.Json))!, p => p.Id == proveedorA.Id);
        Assert.Empty((await b.Http.GetFromJsonAsync<List<CompraResumenDto>>("/api/compras", ApiTestHelpers.Json))!);

        var intactoA = await ObtenerProductoAsync(a, productoA.Id);
        Assert.Equal(2m, intactoA.StockActual);
        Assert.True((await a.Http.GetFromJsonAsync<ProveedorDto>($"/api/proveedores/{proveedorA.Id}", ApiTestHelpers.Json))!.EstaActivo);
    }

    // ---------- Proveedores y permisos ----------

    [Fact]
    public async Task Proveedor_con_CUIT_repetido_en_el_mismo_comercio_devuelve_409_y_dado_de_baja_no_admite_compras()
    {
        var a = await factory.RegistrarComercioAsync();
        var proveedor = await CrearProveedorAsync(a);
        var producto = await a.CrearProductoOkAsync("SKU-BAJA", precioCompra: 100m);

        var conGuiones = $"{proveedor.Cuit[..2]}-{proveedor.Cuit[2..10]}-{proveedor.Cuit[10..]}";
        var repetido = await a.Http.PostAsJsonAsync("/api/proveedores", DatosProveedor("Otra razón social", conGuiones));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PostAsJsonAsync("/api/proveedores", DatosProveedor("CUIT corto", "123"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/proveedores/{proveedor.Id}/estado", new { activo = false })).StatusCode);
        var compra = await RegistrarCompraAsync(a, proveedor.Id, "BAJA-1", "CuentaCorrienteProveedor", null, (producto.Id, 1m, 100m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, compra.StatusCode);
    }

    [Fact]
    public async Task Cajero_no_accede_a_compras_ni_proveedores_pero_si_a_la_alerta_de_stock_bajo()
    {
        var a = await factory.RegistrarComercioAsync();
        var alta = await a.Http.PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Cajero",
            nombreUsuario = "cajero1",
            email = (string?)null,
            password = "Cajero-2026-segura",
            rolId = await a.RolIdAsync("Cajero")
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var anonimo = factory.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = a.Slug, username = "cajero1", password = "Cajero-2026-segura" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        using var cajero = factory.CrearClienteConToken(auth.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/proveedores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/compras")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PostAsJsonAsync("/api/proveedores", DatosProveedor("X", "30712345679"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cajero.GetAsync("/api/productos/stock-bajo")).StatusCode);
    }

    // ---------- Helpers ----------

    private static object DatosProveedor(string razonSocial, string cuit) =>
        new { razonSocial, cuit, telefono = "11-5555-0000", email = "ventas@proveedor.com", direccion = "Av. Siempre Viva 123" };

    private static async Task<ProveedorDto> CrearProveedorAsync(ComercioDePrueba comercio, string? cuit = null)
    {
        cuit ??= Random.Shared.NextInt64(20_000_000_000, 34_999_999_999).ToString();
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/proveedores", DatosProveedor($"Proveedor {cuit}", cuit));
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ProveedorDto>(ApiTestHelpers.Json))!;
    }

    private static Task<HttpResponseMessage> RegistrarCompraAsync(
        ComercioDePrueba comercio, Guid proveedorId, string comprobante, string formaPago, Guid? cajaSesionId,
        params (Guid ProductoId, decimal Cantidad, decimal Costo)[] items)
        => comercio.Http.PostAsJsonAsync("/api/compras", new
        {
            proveedorId,
            numeroComprobante = comprobante,
            fechaCompra = Hoy,
            formaPago,
            cajaSesionId,
            observaciones = (string?)null,
            items = items.Select(i => new { productoId = i.ProductoId, cantidad = i.Cantidad, costoUnitario = i.Costo })
        }, ApiTestHelpers.Json);

    private static async Task<ProductoDto> ObtenerProductoAsync(ComercioDePrueba comercio, Guid id) =>
        (await comercio.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{id}", ApiTestHelpers.Json))!;

    private static async Task<ProductoDto> CrearConStockAsync(ComercioDePrueba comercio, string sku, decimal stock, decimal minimo)
    {
        var producto = await comercio.CrearProductoOkAsync(sku, stockInicial: stock);
        var respuesta = await comercio.Http.PutAsJsonAsync($"/api/productos/{producto.Id}/stock-minimo", new { stockMinimo = minimo });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ProductoDto>(ApiTestHelpers.Json))!;
    }
}
