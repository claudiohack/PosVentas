using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Clientes;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>Helpers compartidos para escenarios con facturación electrónica, clientes y usuarios con rol.</summary>
public static class FiscalTestHelpers
{
    public const int PuntoVenta = 3;

    /// <summary>Comercio con datos fiscales y un certificado autofirmado (ARCA está simulada).</summary>
    public static async Task<ComercioDePrueba> RegistrarComercioFacturandoAsync(this PosVentasApiFactory factory, string cuit, string condicionIva)
    {
        var comercio = await factory.RegistrarComercioAsync();
        var datos = await comercio.Http.PutAsJsonAsync("/api/comercio/configuracion-fiscal", new
        {
            cuit, condicionIva, puntoVenta = PuntoVenta, domicilioFiscal = "Av. Corrientes 1234", ingresosBrutos = (string?)null,
            inicioActividades = (DateOnly?)null, produccion = false
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, datos.StatusCode);

        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest($"CN=posventas-test, SERIALNUMBER=CUIT {cuit}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var carga = await comercio.Http.PutAsJsonAsync("/api/comercio/certificado-afip", new
        {
            formato = "Pkcs12", pkcs12Base64 = Convert.ToBase64String(certificado.Export(X509ContentType.Pkcs12, "clave")), password = "clave"
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, carga.StatusCode);
        return comercio;
    }

    public static async Task<ClienteDto> CrearClienteFiscalAsync(
        this ComercioDePrueba comercio, string documento, string condicionIva, decimal limiteCredito = 0m)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/clientes", new
        {
            razonSocial = $"Cliente {Guid.NewGuid():N}"[..20],
            documento,
            condicionIva,
            telefono = (string?)null,
            email = (string?)null,
            direccion = (string?)null,
            permiteCuentaCorriente = limiteCredito > 0,
            limiteCredito
        }, ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ClienteDto>(ApiTestHelpers.Json))!;
    }

    /// <summary>Crea un usuario con el rol indicado y devuelve un cliente HTTP con su sesión.</summary>
    public static async Task<HttpClient> LoginConRolAsync(this PosVentasApiFactory factory, ComercioDePrueba comercio, string usuario, string rol)
    {
        const string clave = "Usuario-2026-segura";
        var alta = await comercio.Http.PostAsJsonAsync("/api/usuarios",
            new { nombreCompleto = usuario, nombreUsuario = usuario, email = (string?)null, password = clave, rol }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var anonimo = factory.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = comercio.Slug, username = usuario, password = clave });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        return factory.CrearClienteConToken(auth.AccessToken);
    }
}
