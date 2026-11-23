using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PosVentas.Application.Features.Categorias;
using PosVentas.Application.Features.Productos;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class CatalogoAbmTests(PosVentasApiFactory factory)
{
    [Fact]
    public async Task ABM_de_categorias_edita_reordena_y_no_da_de_baja_con_productos_activos()
    {
        var a = await factory.RegistrarComercioAsync();
        var general = await a.CategoriaGeneralAsync(); // antes de crear otras: el helper espera una sola
        var bebidas = await CrearCategoriaAsync(a, "Bebidas");
        var almacen = await CrearCategoriaAsync(a, "Almacén");

        var editada = await a.Http.PutAsJsonAsync($"/api/categorias/{bebidas}", new { nombre = "Bebidas sin alcohol", descripcion = "Gaseosas y aguas", ordenDisplay = 5 });
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal("Bebidas sin alcohol", (await editada.Content.ReadFromJsonAsync<CategoriaDto>(ApiTestHelpers.Json))!.Nombre);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Http.PutAsJsonAsync($"/api/categorias/{almacen}",
            new { nombre = "Bebidas sin alcohol", descripcion = (string?)null, ordenDisplay = 0 })).StatusCode);

        // El orden de la lista define el orden de visualización
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync("/api/categorias/orden", new { ids = new[] { almacen, general, bebidas } })).StatusCode);
        var ordenadas = (await a.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", ApiTestHelpers.Json))!;
        Assert.Equal([almacen, general, bebidas], ordenadas.Select(c => c.Id));

        // Con un producto activo no se puede dar de baja; sin productos, sí
        var producto = await CrearProductoAsync(a, "COCA-15", almacen, codigoBarras: "7790895000997");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.PatchAsJsonAsync($"/api/categorias/{almacen}/estado", new { activa = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/categorias/{bebidas}/estado", new { activa = false })).StatusCode);
        Assert.DoesNotContain((await a.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", ApiTestHelpers.Json))!, c => c.Id == bebidas);
        Assert.Contains((await a.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias?incluirInactivas=true", ApiTestHelpers.Json))!, c => c.Id == bebidas);

        // Una categoría dada de baja no admite productos nuevos
        var enBaja = await a.Http.PostAsJsonAsync("/api/productos", Producto("SKU-BAJA", bebidas));
        Assert.Equal(HttpStatusCode.NotFound, enBaja.StatusCode);
        Assert.NotEqual(Guid.Empty, producto.Id);
    }

    [Fact]
    public async Task Alta_de_producto_con_codigo_de_barras_stock_inicial_y_precio_fijo_y_edicion_completa()
    {
        var a = await factory.RegistrarComercioAsync();
        var general = await a.CategoriaGeneralAsync();
        var bebidas = await CrearCategoriaAsync(a, "Bebidas");

        var alta = await a.Http.PostAsJsonAsync("/api/productos", new
        {
            sku = "agua-500", nombre = "Agua mineral 500 ml", codigoBarras = "7790315000017", precioCompra = 400m, moneda = "ARS",
            porcentajeGanancia = 0m, precioVenta = 650m, porcentajeIva = 21m, categoriaId = general, venderSinStock = false,
            stockMinimo = 12m, stockInicial = 48m
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = (await alta.Content.ReadFromJsonAsync<ProductoDto>(ApiTestHelpers.Json))!;
        Assert.Equal(("AGUA-500", "7790315000017", 48m, 12m), (creado.Sku, creado.CodigoBarras, creado.StockActual, creado.StockMinimo));
        Assert.Equal((400m, 650m, 62.5m), (creado.PrecioCompra, creado.PrecioVenta, creado.PorcentajeGanancia)); // margen recalculado

        // El lector de códigos del POS busca por código de barras o SKU exactos
        Assert.Equal(creado.Id, Assert.Single((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=7790315000017", ApiTestHelpers.Json))!).Id);
        Assert.Equal(creado.Id, Assert.Single((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=agua-500", ApiTestHelpers.Json))!).Id);

        // Código de barras repetido → 409; IVA fuera de las alícuotas → 400
        Assert.Equal(HttpStatusCode.Conflict, (await a.Http.PostAsJsonAsync("/api/productos", Producto("OTRO", general, "7790315000017"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PostAsJsonAsync("/api/productos", Producto("IVA-MAL", general, iva: 19m))).StatusCode);

        // Edición: nombre, categoría, código, costo y margen (el precio sale del margen)
        var edicion = await a.Http.PutAsJsonAsync($"/api/productos/{creado.Id}", new
        {
            sku = "AGUA-500", nombre = "Agua mineral sin gas 500 ml", codigoBarras = "7790315000024", categoriaId = bebidas,
            precioCompra = 500m, porcentajeGanancia = 40m, precioVenta = (decimal?)null, porcentajeIva = 21m, venderSinStock = true, stockMinimo = 24m
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, edicion.StatusCode);
        var editado = (await edicion.Content.ReadFromJsonAsync<ProductoDto>(ApiTestHelpers.Json))!;
        Assert.Equal(("Agua mineral sin gas 500 ml", "Bebidas", "7790315000024", 700m, true, 48m),
            (editado.Nombre, editado.Categoria, editado.CodigoBarras, editado.PrecioVenta, editado.VenderSinStock, editado.StockActual));

        // Baja lógica: deja de aparecer en el listado activo
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/productos/{creado.Id}/estado", new { activo = false })).StatusCode);
        Assert.DoesNotContain((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos", ApiTestHelpers.Json))!, p => p.Id == creado.Id);

        // Un cajero no edita el catálogo
        using var cajero = await factory.LoginConRolAsync(a, "cajero1", "Cajero");
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PostAsJsonAsync("/api/productos", Producto("CAJ-1", general))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PutAsJsonAsync($"/api/categorias/{bebidas}", new { nombre = "X", descripcion = (string?)null, ordenDisplay = 0 })).StatusCode);
    }

    // c) Aislamiento multi-tenant del ABM
    [Fact]
    public async Task Comercio_B_recibe_404_al_editar_dar_de_baja_o_usar_categorias_y_productos_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var categoriaA = await CrearCategoriaAsync(a, "Fiambrería");
        var productoA = await CrearProductoAsync(a, "JAMON-1", categoriaA, codigoBarras: "2000000000015");
        var categoriaB = await b.CategoriaGeneralAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"/api/categorias/{categoriaA}", new { nombre = "Hackeada", descripcion = (string?)null, ordenDisplay = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PatchAsJsonAsync($"/api/categorias/{categoriaA}/estado", new { activa = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync("/api/categorias/orden", new { ids = new[] { categoriaB, categoriaA } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"/api/productos/{productoA.Id}", new
        {
            sku = "JAMON-1", nombre = "Robado", codigoBarras = (string?)null, categoriaId = categoriaB, precioCompra = 1m, porcentajeGanancia = 0m,
            precioVenta = (decimal?)null, porcentajeIva = 21m, venderSinStock = false, stockMinimo = 0m
        }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PatchAsJsonAsync($"/api/productos/{productoA.Id}/estado", new { activo = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PostAsJsonAsync("/api/productos", Producto("B-1", categoriaA))).StatusCode);

        // Mismo SKU y mismo código de barras en otro comercio: permitidos (únicos por comercio)
        Assert.Equal(HttpStatusCode.Created, (await b.Http.PostAsJsonAsync("/api/productos", Producto("JAMON-1", categoriaB, "2000000000015"))).StatusCode);
        Assert.Empty((await b.Http.GetFromJsonAsync<List<ProductoDto>>($"/api/productos?buscar=Fiambr", ApiTestHelpers.Json))!);
        Assert.DoesNotContain((await b.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias?incluirInactivas=true", ApiTestHelpers.Json))!, c => c.Id == categoriaA);

        // A queda intacto
        var intacto = (await a.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{productoA.Id}", ApiTestHelpers.Json))!;
        Assert.Equal(("Producto JAMON-1", true, "Fiambrería"), (intacto.Nombre, intacto.EstaActivo, intacto.Categoria));
    }

    // ---------- Helpers ----------

    private static object Producto(string sku, Guid categoriaId, string? codigoBarras = null, decimal iva = 21m) => new
    {
        sku, nombre = $"Producto {sku}", codigoBarras, precioCompra = 1000m, moneda = "ARS", porcentajeGanancia = 30m,
        porcentajeIva = iva, categoriaId, venderSinStock = false, stockMinimo = 0m, stockInicial = 0m
    };

    private static async Task<Guid> CrearCategoriaAsync(ComercioDePrueba comercio, string nombre)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/categorias", new { nombre, descripcion = (string?)null, ordenDisplay = 1 });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private static async Task<ProductoDto> CrearProductoAsync(ComercioDePrueba comercio, string sku, Guid categoriaId, string? codigoBarras = null)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/productos", Producto(sku, categoriaId, codigoBarras));
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ProductoDto>(ApiTestHelpers.Json))!;
    }
}
