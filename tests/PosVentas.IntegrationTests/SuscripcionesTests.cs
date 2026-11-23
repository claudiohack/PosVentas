using System.Net;
using System.Net.Http.Json;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Suscripciones;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Seguridad;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class SuscripcionesTests(PosVentasApiFactory factory)
{
    [Fact]
    public async Task Un_comercio_nuevo_arranca_con_30_dias_de_prueba_del_plan_predeterminado()
    {
        var a = await factory.RegistrarComercioAsync();

        var resumen = a.Auth.Suscripcion;
        Assert.Equal((EstadoSuscripcion.Prueba, true, 30, "Full"), (resumen.Estado, resumen.EnPrueba, resumen.DiasRestantes, resumen.Plan));
        Assert.Contains(Permisos.ReportesVer, a.Auth.Permisos);

        var mia = await MiSuscripcionAsync(a);
        Assert.Equal("FULL", mia.Plan.Codigo);
        Assert.Equal((1, 0), (mia.UsuariosActivos, mia.ProductosActivos));
        Assert.Contains(mia.PlanesDisponibles, p => p.Codigo == "INICIAL");
        Assert.Contains(mia.PlanesDisponibles, p => p.Codigo == "COMERCIO");
        Assert.Empty(mia.Pagos);
    }

    [Fact]
    public async Task Los_limites_del_plan_frenan_altas_de_usuarios_y_productos_y_los_modulos_ausentes_quitan_sus_permisos()
    {
        var a = await factory.RegistrarComercioAsync();
        var plan = await CrearPlanAsync(maxUsuarios: 2, maxProductos: 2);
        await CambiarPlanAsync(a, plan.Id);

        // Sin módulos: ni el Administrador ve reportes, compras ni planillas; lo básico sigue
        var sesion = (await a.Http.GetFromJsonAsync<SesionDto>("/api/auth/sesion", ApiTestHelpers.Json))!;
        Assert.DoesNotContain(Permisos.ReportesVer, sesion.Permisos);
        Assert.DoesNotContain(Permisos.ComprasConsultar, sesion.Permisos);
        Assert.DoesNotContain(Permisos.ProductosImportar, sesion.Permisos);
        Assert.Contains(Permisos.VentasRegistrar, sesion.Permisos);
        Assert.Contains(Permisos.UsuariosGestionar, sesion.Permisos);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/reportes/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/proveedores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/productos/exportar")).StatusCode);

        // Usuarios: el admin + 1 = 2 (tope)
        var cajero = await a.RolIdAsync("Cajero");
        Assert.Equal(HttpStatusCode.Created, (await PostUsuario(a, "uno", cajero)).StatusCode);
        var excedido = await PostUsuario(a, "dos", cajero);
        Assert.Equal(HttpStatusCode.Forbidden, excedido.StatusCode);
        Assert.Contains("hasta 2 usuarios", await excedido.Content.ReadAsStringAsync());

        // Productos: 2 activos como máximo; dar de baja libera el lugar y reactivar lo vuelve a exigir
        var p1 = await a.CrearProductoOkAsync("LIM-1");
        await a.CrearProductoOkAsync("LIM-2");
        var categoria = await a.CategoriaGeneralAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.CrearProductoAsync("LIM-3", categoria)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/productos/{p1.Id}/estado", new { activo = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await a.CrearProductoAsync("LIM-3", categoria)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.PatchAsJsonAsync($"/api/productos/{p1.Id}/estado", new { activo = true })).StatusCode);

        var mia = await MiSuscripcionAsync(a);
        Assert.Equal((2, 2, plan.Codigo), (mia.UsuariosActivos, mia.ProductosActivos, mia.Plan.Codigo));

        // Aislamiento: el plan y el uso de un comercio no afectan a otro
        var b = await factory.RegistrarComercioAsync();
        var deB = await MiSuscripcionAsync(b);
        Assert.Equal(("FULL", 1, 0), (deB.Plan.Codigo, deB.UsuariosActivos, deB.ProductosActivos));
        Assert.Contains(Permisos.ReportesVer, b.Auth.Permisos);
    }

    [Fact]
    public async Task Sin_el_modulo_de_facturacion_no_se_emiten_facturas_ni_se_configura_ARCA()
    {
        var a = await factory.RegistrarComercioFacturandoAsync("20111111112", "ResponsableInscripto");
        var producto = await a.CrearProductoOkAsync("FAC-PLAN", precioCompra: 1000m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(0m);
        await CambiarPlanAsync(a, (await CrearPlanAsync(maxUsuarios: null, maxProductos: null)).Id);

        var factura = await a.Http.PostAsJsonAsync("/api/ventas", new
        {
            cajaSesionId = sesion.Id, clienteId = (Guid?)null, tipoComprobante = "FacturaB",
            items = new[] { new { productoId = producto.Id, cantidad = 1m } },
            pagos = new[] { new { medio = "Efectivo", monto = 1000m } }
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, factura.StatusCode);
        Assert.Contains("facturación electrónica", await factura.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/comercio/configuracion-fiscal")).StatusCode);

        // El ticket sigue funcionando
        Assert.Equal(HttpStatusCode.Created, (await a.VenderAsync(sesion.Id, null, [(producto.Id, 1m)], [("Efectivo", 1000m)])).StatusCode);
    }

    [Fact]
    public async Task Al_vencer_hay_gracia_luego_solo_lectura_y_un_pago_lo_reactiva()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("VTO-1");
        try
        {
            // Día 31: venció la prueba, corre la gracia: opera normalmente, con aviso
            factory.Reloj.Adelantar(TimeSpan.FromDays(31));
            var enGracia = await MiSuscripcionAsync(a);
            Assert.Equal((EstadoSuscripcion.EnGracia, 6), (enGracia.Resumen.Estado, enGracia.Resumen.DiasRestantes));
            Assert.Equal(HttpStatusCode.Created, (await a.CrearProductoAsync("VTO-2", await a.CategoriaGeneralAsync())).StatusCode);

            // Día 38: solo lectura. Se ingresa, se consulta y se exporta; no se registra nada
            factory.Reloj.Adelantar(TimeSpan.FromDays(7));
            using var anonimo = factory.CreateClient();
            var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = a.Slug, username = "admin", password = a.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(EstadoSuscripcion.SoloLectura, (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!.Suscripcion.Estado);

            Assert.Equal(HttpStatusCode.OK, (await a.Http.GetAsync($"/api/productos/{producto.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.Http.GetAsync("/api/productos/exportar")).StatusCode);
            var bloqueada = await a.CrearProductoAsync("VTO-3", await a.CategoriaGeneralAsync());
            Assert.Equal(HttpStatusCode.PaymentRequired, bloqueada.StatusCode);
            Assert.Contains("suscripción está vencida", await bloqueada.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.PaymentRequired, (await a.Http.PatchAsJsonAsync($"/api/productos/{producto.Id}/estado", new { activo = false })).StatusCode);

            // HS Tech registra el pago de 1 mes: corre desde hoy (ya estaba vencida) y vuelve a operar
            using var plataforma = await factory.ClientePlataformaAsync();
            var pago = await plataforma.PostAsJsonAsync($"/api/plataforma/comercios/{a.TenantId}/pagos",
                new { meses = 1, monto = (decimal?)null, nota = "Transferencia" }, ApiTestHelpers.Json);
            Assert.True(pago.StatusCode == HttpStatusCode.OK, await pago.Content.ReadAsStringAsync());
            var fila = (await pago.Content.ReadFromJsonAsync<ComercioSuscripcionDto>(ApiTestHelpers.Json))!;
            Assert.Equal((EstadoSuscripcion.Activa, false), (fila.Estado, fila.EnPrueba));
            Assert.InRange(fila.VenceUtc, factory.Reloj.AhoraUtc.AddDays(27), factory.Reloj.AhoraUtc.AddDays(32));

            Assert.Equal(HttpStatusCode.Created, (await a.CrearProductoAsync("VTO-3", await a.CategoriaGeneralAsync())).StatusCode);
            var pagos = (await MiSuscripcionAsync(a)).Pagos;
            var unico = Assert.Single(pagos);
            Assert.Equal((55000m, 1, OrigenPagoSuscripcion.Manual, "Transferencia"), (unico.Monto, unico.Meses, unico.Origen, unico.Nota));
        }
        finally
        {
            factory.Reloj.Restablecer();
        }
    }

    [Fact]
    public async Task Solo_HS_Tech_administra_planes_y_el_plan_predeterminado_define_la_prueba_de_los_nuevos()
    {
        var a = await factory.RegistrarComercioAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/plataforma/planes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/plataforma/comercios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.PostAsJsonAsync($"/api/plataforma/comercios/{a.TenantId}/pagos",
            new { meses = 12, monto = 0m, nota = (string?)null }, ApiTestHelpers.Json)).StatusCode);

        using var plataforma = await factory.ClientePlataformaAsync();
        var planes = (await plataforma.GetFromJsonAsync<List<PlanDto>>("/api/plataforma/planes", ApiTestHelpers.Json))!;
        var full = Assert.Single(planes, p => p.Codigo == "FULL");
        Assert.True(full.EsPredeterminado);

        // Validaciones: módulo inexistente (400), código repetido (409), quitar el predeterminado sin reemplazo (422)
        Assert.Equal(HttpStatusCode.BadRequest, (await plataforma.PostAsJsonAsync("/api/plataforma/planes", new
        {
            codigo = "MALO", nombre = "Malo", descripcion = (string?)null, precioMensual = 1m, maxUsuarios = (int?)null,
            maxProductos = (int?)null, modulos = new[] { "inventado" }, orden = 9
        }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await plataforma.PostAsJsonAsync("/api/plataforma/planes", new
        {
            codigo = "full", nombre = "Otro", descripcion = (string?)null, precioMensual = 1m, maxUsuarios = (int?)null,
            maxProductos = (int?)null, modulos = Array.Empty<string>(), orden = 9
        }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PutPlan(plataforma, full, esPredeterminado: false)).StatusCode);

        var nuevo = await CrearPlanAsync(maxUsuarios: 3, maxProductos: 100);
        try
        {
            // El nuevo pasa a ser el de la prueba gratuita; el comercio que se registra ahora lo recibe
            var marcado = await PutPlan(plataforma, nuevo, esPredeterminado: true, precio: 12345.50m);
            Assert.Equal(HttpStatusCode.OK, marcado.StatusCode);
            Assert.Equal(12345.50m, (await marcado.Content.ReadFromJsonAsync<PlanDto>(ApiTestHelpers.Json))!.PrecioMensual);

            var b = await factory.RegistrarComercioAsync();
            Assert.Equal(nuevo.Nombre, b.Auth.Suscripcion.Plan);
            Assert.DoesNotContain(Permisos.ReportesVer, b.Auth.Permisos);

            var comercios = (await plataforma.GetFromJsonAsync<List<ComercioSuscripcionDto>>("/api/plataforma/comercios", ApiTestHelpers.Json))!;
            Assert.Equal(nuevo.Id, Assert.Single(comercios, c => c.ComercioId == b.TenantId).PlanId);
            Assert.DoesNotContain(comercios, c => c.Slug == PosVentasApiFactory.SlugPlataforma);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await PutPlan(plataforma, full, esPredeterminado: true)).StatusCode);
        }
    }

    // ---------- Helpers ----------

    private static async Task<MiSuscripcionDto> MiSuscripcionAsync(ComercioDePrueba comercio) =>
        (await comercio.Http.GetFromJsonAsync<MiSuscripcionDto>("/api/suscripcion", ApiTestHelpers.Json))!;

    private async Task<PlanDto> CrearPlanAsync(int? maxUsuarios, int? maxProductos, params string[] modulos)
    {
        using var plataforma = await factory.ClientePlataformaAsync();
        var codigo = $"TEST_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var respuesta = await plataforma.PostAsJsonAsync("/api/plataforma/planes", new
        {
            codigo, nombre = $"Plan {codigo}", descripcion = "Plan de prueba", precioMensual = 1000m,
            maxUsuarios, maxProductos, modulos, orden = 50
        }, ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<PlanDto>(ApiTestHelpers.Json))!;
    }

    private async Task CambiarPlanAsync(ComercioDePrueba comercio, Guid planId)
    {
        using var plataforma = await factory.ClientePlataformaAsync();
        var respuesta = await plataforma.PutAsJsonAsync($"/api/plataforma/comercios/{comercio.TenantId}/plan", new { planId }, ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PutPlan(HttpClient plataforma, PlanDto plan, bool esPredeterminado, decimal? precio = null) =>
        plataforma.PutAsJsonAsync($"/api/plataforma/planes/{plan.Id}", new
        {
            nombre = plan.Nombre, descripcion = plan.Descripcion, precioMensual = precio ?? plan.PrecioMensual,
            maxUsuarios = plan.MaxUsuarios, maxProductos = plan.MaxProductos, modulos = plan.Modulos, orden = plan.Orden,
            estaActivo = true, esPredeterminado
        }, ApiTestHelpers.Json);

    private static Task<HttpResponseMessage> PostUsuario(ComercioDePrueba comercio, string usuario, Guid rolId) =>
        comercio.Http.PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = usuario, nombreUsuario = usuario, email = (string?)null, password = "Clave-Segura-123", rolId
        }, ApiTestHelpers.Json);
}
