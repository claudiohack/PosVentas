using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Categorias;
using PosVentas.Application.Features.Productos;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>Comercio de prueba registrado vía API, con su cliente HTTP ya autenticado.</summary>
public sealed record ComercioDePrueba(AuthResponse Auth, HttpClient Http, string Slug, string Password)
{
    public Guid TenantId => Auth.ComercioId;
}

public static class ApiTestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<ComercioDePrueba> RegistrarComercioAsync(this PosVentasApiFactory factory, string nombre = "Comercio de prueba")
    {
        var slug = $"test-{Guid.NewGuid():N}";
        var password = $"Clave-Segura-{Guid.NewGuid():N}"[..24];

        using var anonimo = factory.CreateClient();
        var respuesta = await anonimo.PostAsJsonAsync("/api/auth/registro", new
        {
            nombreComercio = nombre,
            slug,
            cuit = (string?)null,
            nombreAdministrador = "Admin de prueba",
            username = "admin",
            password
        }, Json);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var auth = (await respuesta.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        return new ComercioDePrueba(auth, factory.CrearClienteConToken(auth.AccessToken), slug, password);
    }

    public static HttpClient CrearClienteConToken(this PosVentasApiFactory factory, string token)
    {
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    public static async Task<Guid> CategoriaGeneralAsync(this ComercioDePrueba comercio)
    {
        var categorias = await comercio.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", Json);
        return Assert.Single(categorias!).Id;
    }

    public static Task<HttpResponseMessage> CrearProductoAsync(
        this ComercioDePrueba comercio, string sku, Guid categoriaId,
        decimal precioCompra = 1000m, decimal ganancia = 30m, decimal iva = 21m, bool venderSinStock = false)
        => comercio.Http.PostAsJsonAsync("/api/productos", new
        {
            sku,
            nombre = $"Producto {sku}",
            precioCompra,
            moneda = "ARS",
            porcentajeGanancia = ganancia,
            porcentajeIva = iva,
            categoriaId,
            venderSinStock
        }, Json);

    public static async Task<ProductoDto> CrearProductoOkAsync(
        this ComercioDePrueba comercio, string sku, decimal precioCompra = 1000m, decimal ganancia = 30m,
        decimal iva = 21m, decimal stockInicial = 0m)
    {
        var categoria = await comercio.CategoriaGeneralAsync();
        var respuesta = await comercio.CrearProductoAsync(sku, categoria, precioCompra, ganancia, iva);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var producto = (await respuesta.Content.ReadFromJsonAsync<ProductoDto>(Json))!;

        if (stockInicial > 0)
        {
            var stock = await comercio.Http.PostAsJsonAsync($"/api/productos/{producto.Id}/stock/ingreso",
                new { cantidad = stockInicial }, Json);
            Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        }

        return producto;
    }
}
