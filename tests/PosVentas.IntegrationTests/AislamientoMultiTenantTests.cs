using System.Net;
using System.Net.Http.Json;
using PosVentas.Application.Features.Productos;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class AislamientoMultiTenantTests(PosVentasApiFactory factory)
{
    [Fact]
    public async Task TenantB_recibe_404_al_leer_un_producto_del_TenantA()
    {
        var a = await factory.RegistrarComercioAsync("Comercio A");
        var b = await factory.RegistrarComercioAsync("Comercio B");
        var productoA = await a.CrearProductoOkAsync("SKU-AISLADO");

        var respuestaA = await a.Http.GetAsync($"/api/productos/{productoA.Id}");
        var respuestaB = await b.Http.GetAsync($"/api/productos/{productoA.Id}");

        Assert.Equal(HttpStatusCode.OK, respuestaA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, respuestaB.StatusCode);
    }

    [Fact]
    public async Task TenantB_recibe_404_al_modificar_el_stock_de_un_producto_del_TenantA()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var productoA = await a.CrearProductoOkAsync("SKU-STOCK", stockInicial: 5);

        var respuestaB = await b.Http.PostAsJsonAsync($"/api/productos/{productoA.Id}/stock/ingreso",
            new { cantidad = 100 }, ApiTestHelpers.Json);

        Assert.Equal(HttpStatusCode.NotFound, respuestaB.StatusCode);

        // El stock de A no cambió
        var productoActual = await a.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{productoA.Id}", ApiTestHelpers.Json);
        Assert.Equal(5m, productoActual!.StockActual);
    }

    [Fact]
    public async Task El_listado_de_cada_tenant_solo_contiene_sus_productos()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var productoA = await a.CrearProductoOkAsync("SKU-LISTA-A");
        var productoB = await b.CrearProductoOkAsync("SKU-LISTA-B");

        var listaA = await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos", ApiTestHelpers.Json);
        var listaB = await b.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos", ApiTestHelpers.Json);

        Assert.Equal(productoA.Id, Assert.Single(listaA!).Id);
        Assert.Equal(productoB.Id, Assert.Single(listaB!).Id);
    }

    [Fact]
    public async Task TenantB_no_puede_usar_una_categoria_del_TenantA()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var categoriaA = await a.CategoriaGeneralAsync();

        var respuesta = await b.CrearProductoAsync("SKU-INTRUSO", categoriaA);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Dos_tenants_pueden_registrar_el_mismo_SKU_sin_colisiones()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();

        var respuestaA = await a.CrearProductoAsync("7790001000017", await a.CategoriaGeneralAsync());
        var respuestaB = await b.CrearProductoAsync("7790001000017", await b.CategoriaGeneralAsync());

        Assert.Equal(HttpStatusCode.Created, respuestaA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, respuestaB.StatusCode);
    }

    [Fact]
    public async Task El_mismo_SKU_dentro_de_un_tenant_devuelve_409()
    {
        var a = await factory.RegistrarComercioAsync();
        var categoria = await a.CategoriaGeneralAsync();

        var primero = await a.CrearProductoAsync("SKU-DUPLICADO", categoria);
        var segundo = await a.CrearProductoAsync("sku-duplicado", categoria); // se normaliza a mayúsculas

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    }

    [Fact]
    public async Task Dos_tenants_pueden_tener_el_mismo_nombre_de_usuario_y_cada_login_resuelve_su_comercio()
    {
        // Ambos comercios se registran con el usuario "admin"
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        using var anonimo = factory.CreateClient();

        var loginA = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = a.Slug, username = "admin", password = a.Password });
        var loginB = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = b.Slug, username = "admin", password = b.Password });
        var cruzado = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = b.Slug, username = "admin", password = a.Password });

        Assert.Equal(HttpStatusCode.OK, loginA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cruzado.StatusCode);

        var authA = await loginA.Content.ReadFromJsonAsync<Application.Features.Auth.AuthResponse>(ApiTestHelpers.Json);
        var authB = await loginB.Content.ReadFromJsonAsync<Application.Features.Auth.AuthResponse>(ApiTestHelpers.Json);
        Assert.Equal(a.TenantId, authA!.ComercioId);
        Assert.Equal(b.TenantId, authB!.ComercioId);
    }
}
