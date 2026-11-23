using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Cajas;
using PosVentas.Application.Features.Categorias;
using PosVentas.Application.Features.Clientes;
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

    private static readonly SemaphoreSlim CandadoPlataforma = new(1, 1);
    private static string? _tokenPlataforma;

    /// <summary>Cliente autenticado como administrador de la plataforma (HS Tech). El login se hace una vez por corrida.</summary>
    public static async Task<HttpClient> ClientePlataformaAsync(this PosVentasApiFactory factory)
    {
        await CandadoPlataforma.WaitAsync();
        try
        {
            if (_tokenPlataforma is null)
            {
                using var anonimo = factory.CreateClient();
                var login = await anonimo.PostAsJsonAsync("/api/auth/login", new
                {
                    comercio = PosVentasApiFactory.SlugPlataforma,
                    username = PosVentasApiFactory.UsuarioPlataforma,
                    password = factory.PasswordPlataforma
                });
                Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
                _tokenPlataforma = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!.AccessToken;
            }
            return factory.CrearClienteConToken(_tokenPlataforma);
        }
        finally
        {
            CandadoPlataforma.Release();
        }
    }

    /// <summary>Emite una invitación como administrador de la plataforma y devuelve su token.</summary>
    public static async Task<string> CrearInvitacionAsync(this PosVentasApiFactory factory, string? email = null)
    {
        using var plataforma = await factory.ClientePlataformaAsync();
        var respuesta = await plataforma.PostAsJsonAsync("/api/auth/invitaciones", new { emailDestinatario = email, nota = "test", diasVigencia = 7 });
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<InvitacionCreadaDto>(Json))!.Token;
    }

    /// <summary>Alta de un comercio por el flujo real: invitación de HS Tech + registro con el token.</summary>
    public static async Task<ComercioDePrueba> RegistrarComercioAsync(this PosVentasApiFactory factory, string nombre = "Comercio de prueba")
    {
        var slug = $"test-{Guid.NewGuid():N}";
        var password = $"Clave-Segura-{Guid.NewGuid():N}"[..24];
        var token = await factory.CrearInvitacionAsync();

        using var anonimo = factory.CreateClient();
        var respuesta = await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", new
        {
            tokenInvitacion = token,
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

    /// <summary>Id del rol del comercio con ese nombre ("Administrador", "Supervisor", "Cajero" o uno personalizado).</summary>
    public static async Task<Guid> RolIdAsync(this ComercioDePrueba comercio, string nombre)
    {
        var roles = await comercio.Http.GetFromJsonAsync<List<PosVentas.Application.Features.Roles.RolDto>>("/api/roles", Json);
        return Assert.Single(roles!, r => r.Nombre == nombre).Id;
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

    public static async Task<ClienteDto> CrearClienteAsync(
        this ComercioDePrueba comercio, decimal limiteCredito, bool permiteCuentaCorriente = true, string? documento = null)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/clientes", new
        {
            razonSocial = $"Cliente {Guid.NewGuid():N}"[..20],
            documento = documento ?? Random.Shared.NextInt64(20_000_000_000, 29_999_999_999).ToString(),
            condicionIva = "ConsumidorFinal",
            telefono = (string?)null,
            email = (string?)null,
            direccion = (string?)null,
            permiteCuentaCorriente,
            limiteCredito
        }, Json);

        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ClienteDto>(Json))!;
    }

    public static async Task<CuentaCorrienteDto> CuentaCorrienteAsync(this ComercioDePrueba comercio, Guid clienteId) =>
        (await comercio.Http.GetFromJsonAsync<CuentaCorrienteDto>($"/api/clientes/{clienteId}/cuenta-corriente", Json))!;

    public static async Task<SesionCajaDto> AbrirCajaAsync(this ComercioDePrueba comercio, decimal monto)
    {
        var caja = Assert.Single((await comercio.Http.GetFromJsonAsync<List<CajaDto>>("/api/cajas", Json))!);
        var respuesta = await comercio.Http.PostAsJsonAsync($"/api/cajas/{caja.Id}/apertura", new { montoApertura = monto }, Json);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<SesionCajaDto>(Json))!;
    }

    public static async Task<ResumenSesionDto> ResumenCajaAsync(this ComercioDePrueba comercio, Guid sesionId) =>
        (await comercio.Http.GetFromJsonAsync<ResumenSesionDto>($"/api/cajas/sesiones/{sesionId}/resumen", Json))!;

    public static Task<HttpResponseMessage> VenderAsync(
        this ComercioDePrueba comercio, Guid sesionId, Guid? clienteId,
        (Guid ProductoId, decimal Cantidad)[] items, (string Medio, decimal Monto)[] pagos)
        => comercio.Http.PostAsJsonAsync("/api/ventas", new
        {
            cajaSesionId = sesionId,
            clienteId,
            tipoComprobante = "Ticket",
            items = items.Select(i => new { productoId = i.ProductoId, cantidad = i.Cantidad }),
            pagos = pagos.Select(p => new { medio = p.Medio, monto = p.Monto })
        }, Json);

    public static Task<HttpResponseMessage> CobrarAsync(
        this ComercioDePrueba comercio, Guid clienteId, Guid sesionId, string medio, decimal monto)
        => comercio.Http.PostAsJsonAsync($"/api/clientes/{clienteId}/cobranzas", new
        {
            monto,
            medio,
            cajaSesionId = sesionId,
            observaciones = "entrega a cuenta"
        }, Json);
}
