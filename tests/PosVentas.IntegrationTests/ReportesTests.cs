using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Proveedores;
using PosVentas.Application.Features.Reportes;
using PosVentas.Domain.Enums;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

/// <summary>
/// Escenario con importes calculados a mano:
///   P1 "Auriculares": costo 1000, margen 50 % → neto 1500, IVA 21 % → 1815 final.   Categoría General.
///   P2 "Agua":        costo  200, margen 100 % → neto  400, IVA 0 %  →  400 final.  Categoría Bebidas.
///   V1: 2 × P1                 → neto 3000, IVA 630, total 3630. Paga 4000 en efectivo (vuelto 370).
///   V2: 1 × P1 + 3 × P2        → neto 2700, IVA 315, total 3015. Tarjeta.
///   V3: 5 × P2                 → neto 2000, IVA   0, total 2000. QR 1000 + transferencia 1000.
///   Totales: 3 ventas, total 8645, neto 7700, IVA 945, costo 3×1000 + 8×200 = 4600, margen 3100 (40,26 %).
/// </summary>
[Collection(ApiCollection.Nombre)]
public sealed class ReportesTests(PosVentasApiFactory factory)
{
    // a) Las métricas reflejan con precisión las ventas
    [Fact]
    public async Task Dashboard_refleja_con_precision_ventas_margen_ticket_promedio_y_medios_de_pago()
    {
        var a = await factory.RegistrarComercioAsync();
        var escenario = await CargarEscenarioAsync(a);

        var dashboard = (await a.Http.GetFromJsonAsync<DashboardResumenDto>("/api/reportes/dashboard", ApiTestHelpers.Json))!;

        Assert.Equal(3, dashboard.CantidadVentas);
        Assert.Equal(8645m, dashboard.TotalVendido);
        Assert.Equal(7700m, dashboard.VentaNeta);
        Assert.Equal(945m, dashboard.TotalIva);
        Assert.Equal(4600m, dashboard.CostoEstimado);
        Assert.Equal(3100m, dashboard.MargenBruto);
        Assert.Equal(40.26m, dashboard.MargenPorcentaje);
        Assert.Equal(2881.67m, dashboard.TicketPromedio);

        var medios = dashboard.IngresosPorMedio.ToDictionary(m => m.Medio);
        Assert.Equal(3630m, medios[MedioPago.Efectivo].Total); // 4000 cobrados − 370 de vuelto
        Assert.Equal(3015m, medios[MedioPago.Tarjeta].Total);
        Assert.Equal(1000m, medios[MedioPago.QR].Total);
        Assert.Equal(1000m, medios[MedioPago.Transferencia].Total);
        Assert.Equal(0m, medios[MedioPago.CuentaCorriente].Total);
        Assert.Equal(dashboard.TotalVendido, dashboard.IngresosPorMedio.Sum(m => m.Total));
        Assert.Equal(1, medios[MedioPago.Tarjeta].Operaciones);

        // El costo queda congelado al vender: una compra posterior más cara no cambia el margen ya realizado
        var proveedor = await CrearProveedorAsync(a);
        var compra = await a.Http.PostAsJsonAsync("/api/compras", new
        {
            proveedorId = proveedor.Id, numeroComprobante = "FC-1", fechaCompra = DateOnly.FromDateTime(DateTime.UtcNow),
            formaPago = "CuentaCorrienteProveedor", cajaSesionId = (Guid?)null, observaciones = (string?)null,
            items = new[] { new { productoId = escenario.P1, cantidad = 10m, costoUnitario = 3000m } }
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, compra.StatusCode);
        var despues = (await a.Http.GetFromJsonAsync<DashboardResumenDto>("/api/reportes/dashboard", ApiTestHelpers.Json))!;
        Assert.Equal(3100m, despues.MargenBruto);

        // Un período sin ventas da todo en cero (y no rompe por división por cero)
        var ayer = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)).ToString("yyyy-MM-dd");
        var vacio = (await a.Http.GetFromJsonAsync<DashboardResumenDto>($"/api/reportes/dashboard?desde={ayer}&hasta={ayer}", ApiTestHelpers.Json))!;
        Assert.Equal((0, 0m, 0m, 0m), (vacio.CantidadVentas, vacio.TotalVendido, vacio.TicketPromedio, vacio.MargenPorcentaje));
    }

    [Fact]
    public async Task Top_productos_ordena_por_unidades_o_recaudacion_y_respeta_el_limite()
    {
        var a = await factory.RegistrarComercioAsync();
        var e = await CargarEscenarioAsync(a);

        var porUnidades = (await a.Http.GetFromJsonAsync<TopProductosDto>("/api/reportes/top-productos", ApiTestHelpers.Json))!;
        Assert.Equal([e.P2, e.P1], porUnidades.Productos.Select(p => p.ProductoId));
        var agua = porUnidades.Productos[0];
        Assert.Equal((1, 8m, 3200m, 3200m, 1600m, 1600m, 2), (agua.Posicion, agua.Unidades, agua.Recaudacion, agua.VentaNeta, agua.Costo, agua.Margen, agua.Operaciones));
        Assert.Equal("Bebidas", agua.Categoria);

        var porRecaudacion = (await a.Http.GetFromJsonAsync<TopProductosDto>("/api/reportes/top-productos?ordenarPor=Recaudacion&top=1", ApiTestHelpers.Json))!;
        var auriculares = Assert.Single(porRecaudacion.Productos);
        Assert.Equal((e.P1, 3m, 5445m, 4500m, 3000m, 1500m), (auriculares.ProductoId, auriculares.Unidades, auriculares.Recaudacion, auriculares.VentaNeta, auriculares.Costo, auriculares.Margen));

        var soloBebidas = (await a.Http.GetFromJsonAsync<TopProductosDto>($"/api/reportes/top-productos?categoriaId={e.Bebidas}", ApiTestHelpers.Json))!;
        Assert.Equal(e.P2, Assert.Single(soloBebidas.Productos).ProductoId);

        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.GetAsync("/api/reportes/top-productos?top=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.GetAsync("/api/reportes/top-productos?top=500")).StatusCode);
    }

    [Fact]
    public async Task Rentabilidad_por_categoria_reparte_venta_costo_margen_y_participacion()
    {
        var a = await factory.RegistrarComercioAsync();
        var e = await CargarEscenarioAsync(a);

        var reporte = (await a.Http.GetFromJsonAsync<RentabilidadPorCategoriaDto>("/api/reportes/categorias", ApiTestHelpers.Json))!;

        Assert.Equal(2, reporte.Categorias.Count);
        var general = reporte.Categorias.Single(c => c.Categoria == "General");
        var bebidas = reporte.Categorias.Single(c => c.CategoriaId == e.Bebidas);
        Assert.Equal((4500m, 5445m, 3000m, 1500m, 33.33m, 58.44m), (general.VentaNeta, general.Recaudacion, general.Costo, general.Margen, general.MargenPorcentaje, general.ParticipacionPorcentaje));
        Assert.Equal((3200m, 3200m, 1600m, 1600m, 50m, 41.56m), (bebidas.VentaNeta, bebidas.Recaudacion, bebidas.Costo, bebidas.Margen, bebidas.MargenPorcentaje, bebidas.ParticipacionPorcentaje));
        Assert.Equal(reporte.Categorias[0].CategoriaId, general.CategoriaId); // ordenadas por venta
    }

    [Fact]
    public async Task Reporte_de_cajas_consolida_turnos_cerrados_y_acumula_sobrantes_y_faltantes()
    {
        var a = await factory.RegistrarComercioAsync();
        var e = await CargarEscenarioAsync(a); // turno abierto con fondo 1000 y las 3 ventas

        // Turno 1: esperado 1000 + 3630 en efectivo = 4630; se cuentan 4600 → faltante 30
        Assert.Equal(HttpStatusCode.OK, (await Cerrar(a, e.Sesion, 4600m)).StatusCode);
        // Turno 2: fondo 500 sin ventas; se cuentan 550 → sobrante 50
        var segunda = await a.AbrirCajaAsync(500m);
        Assert.Equal(HttpStatusCode.OK, (await Cerrar(a, segunda.Id, 550m)).StatusCode);
        // Turno 3 queda abierto: no entra en el reporte
        await a.AbrirCajaAsync(0m);

        var reporte = (await a.Http.GetFromJsonAsync<ReporteCierresCajaDto>("/api/reportes/cajas", ApiTestHelpers.Json))!;

        Assert.Equal((2, 0, 1, 1), (reporte.CantidadTurnos, reporte.TurnosExactos, reporte.TurnosConSobrante, reporte.TurnosConFaltante));
        Assert.Equal((50m, 30m, 20m), (reporte.TotalSobrantes, reporte.TotalFaltantes, reporte.DiferenciaNeta));
        Assert.Equal(8645m, reporte.TotalVendido);

        var primero = reporte.Turnos.Single(t => t.SesionId == e.Sesion);
        Assert.Equal((4630m, 4600m, -30m, "Faltante", 3), (primero.EfectivoEsperado, primero.EfectivoContado, primero.Diferencia, primero.Resultado, primero.CantidadVentas));
        Assert.Equal("Admin de prueba", primero.UsuarioCierre);
        Assert.Equal(segunda.Id, reporte.Turnos[0].SesionId); // el cierre más reciente primero

        var porCaja = (await a.Http.GetFromJsonAsync<ReporteCierresCajaDto>($"/api/reportes/cajas?cajaId={primero.CajaId}", ApiTestHelpers.Json))!;
        Assert.Equal(2, porCaja.CantidadTurnos);
    }

    // b) Aislamiento multi-tenant
    [Fact]
    public async Task Comercio_B_no_ve_metricas_ni_reportes_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var e = await CargarEscenarioAsync(a);
        await Cerrar(a, e.Sesion, 4600m);
        var cajaA = (await a.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", ApiTestHelpers.Json))!.Single().Id;

        var dashboardB = (await b.Http.GetFromJsonAsync<DashboardResumenDto>("/api/reportes/dashboard", ApiTestHelpers.Json))!;
        Assert.Equal((0, 0m, 0m, 0m), (dashboardB.CantidadVentas, dashboardB.TotalVendido, dashboardB.MargenBruto, dashboardB.CostoEstimado));
        Assert.All(dashboardB.IngresosPorMedio, m => Assert.Equal(0m, m.Total));
        Assert.Empty((await b.Http.GetFromJsonAsync<TopProductosDto>("/api/reportes/top-productos", ApiTestHelpers.Json))!.Productos);
        Assert.Empty((await b.Http.GetFromJsonAsync<RentabilidadPorCategoriaDto>("/api/reportes/categorias", ApiTestHelpers.Json))!.Categorias);
        var cajasB = (await b.Http.GetFromJsonAsync<ReporteCierresCajaDto>("/api/reportes/cajas", ApiTestHelpers.Json))!;
        Assert.Equal((0, 0m), (cajasB.CantidadTurnos, cajasB.TotalFaltantes));

        // Filtrar por una categoría o una caja del comercio A responde 404 para B
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/reportes/top-productos?categoriaId={e.Bebidas}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/reportes/cajas?cajaId={cajaA}")).StatusCode);

        // Mientras tanto, A sigue viendo lo suyo
        Assert.Equal(8645m, (await a.Http.GetFromJsonAsync<DashboardResumenDto>("/api/reportes/dashboard", ApiTestHelpers.Json))!.TotalVendido);
    }

    [Fact]
    public async Task Reportes_son_solo_para_administradores_y_supervisores_y_validan_el_periodo()
    {
        var a = await factory.RegistrarComercioAsync();
        using var cajero = await LoginConRolAsync(a, "cajero1", "Cajero");
        using var supervisor = await LoginConRolAsync(a, "super1", "Supervisor");

        foreach (var ruta in new[] { "dashboard", "top-productos", "categorias", "cajas" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync($"/api/reportes/{ruta}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/reportes/{ruta}")).StatusCode);
        }

        var invertido = await a.Http.GetAsync("/api/reportes/dashboard?desde=2026-11-10&hasta=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, invertido.StatusCode);
        var problema = (await invertido.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.NotNull(problema["errors"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.GetAsync("/api/reportes/dashboard?desde=2024-01-01&hasta=2026-01-01")).StatusCode);
    }

    // ---------- Escenario ----------

    private sealed record Escenario(Guid P1, Guid P2, Guid Bebidas, Guid Sesion);

    private static async Task<Escenario> CargarEscenarioAsync(ComercioDePrueba a)
    {
        var p1 = (await a.CrearProductoOkAsync("AURIC", precioCompra: 1000m, ganancia: 50m, iva: 21m, stockInicial: 20m)).Id;

        var categoria = await a.Http.PostAsJsonAsync("/api/categorias", new { nombre = "Bebidas", descripcion = (string?)null, ordenDisplay = 1 });
        Assert.Equal(HttpStatusCode.Created, categoria.StatusCode);
        var bebidas = (await categoria.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        var alta = await a.CrearProductoAsync("AGUA", bebidas, precioCompra: 200m, ganancia: 100m, iva: 0m);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var p2 = (await alta.Content.ReadFromJsonAsync<ProductoDto>(ApiTestHelpers.Json))!.Id;
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PostAsJsonAsync($"/api/productos/{p2}/stock/ingreso", new { cantidad = 20m })).StatusCode);

        var sesion = await a.AbrirCajaAsync(1000m);

        await Vender(a, sesion.Id, [(p1, 2m)], [("Efectivo", 4000m)]);
        await Vender(a, sesion.Id, [(p1, 1m), (p2, 3m)], [("Tarjeta", 3015m)]);
        await Vender(a, sesion.Id, [(p2, 5m)], [("QR", 1000m), ("Transferencia", 1000m)]);

        return new Escenario(p1, p2, bebidas, sesion.Id);
    }

    private static async Task Vender(ComercioDePrueba a, Guid sesion, (Guid, decimal)[] items, (string, decimal)[] pagos)
    {
        var respuesta = await a.VenderAsync(sesion, null, items, pagos);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> Cerrar(ComercioDePrueba a, Guid sesion, decimal contado) =>
        a.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesion}/cierre", new { montoCierreReal = contado, observaciones = "cierre" });

    private static async Task<ProveedorDto> CrearProveedorAsync(ComercioDePrueba a)
    {
        var cuit = Random.Shared.NextInt64(20_000_000_000, 34_999_999_999).ToString();
        var respuesta = await a.Http.PostAsJsonAsync("/api/proveedores", new { razonSocial = $"Proveedor {cuit}", cuit, telefono = (string?)null, email = (string?)null, direccion = (string?)null });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ProveedorDto>(ApiTestHelpers.Json))!;
    }

    private async Task<HttpClient> LoginConRolAsync(ComercioDePrueba comercio, string usuario, string rol)
    {
        const string clave = "Usuario-2026-segura";
        var alta = await comercio.Http.PostAsJsonAsync("/api/usuarios",
            new { nombreCompleto = usuario, nombreUsuario = usuario, email = (string?)null, password = clave, rolId = await comercio.RolIdAsync(rol) },
            ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var anonimo = factory.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = comercio.Slug, username = usuario, password = clave });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        return factory.CrearClienteConToken(auth.AccessToken);
    }
}
