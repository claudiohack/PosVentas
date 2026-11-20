using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PosVentas.Application.Common.Security;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class SeguridadJwtTests(PosVentasApiFactory factory)
{
    private const string Issuer = "https://pos.hs-tech.com.ar";
    private const string Audience = "posventas-client";

    [Fact]
    public async Task Peticion_sin_token_devuelve_401()
    {
        using var anonimo = factory.CreateClient();

        var respuesta = await anonimo.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_TenantId_adulterado_devuelve_401()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();

        // Se reemplaza el claim tid por el del comercio B conservando la firma original de A.
        var adulterado = ReemplazarClaim(a.Auth.AccessToken, PosVentasClaims.TenantId, b.TenantId.ToString());
        using var cliente = factory.CrearClienteConToken(adulterado);

        var respuesta = await cliente.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_rol_adulterado_devuelve_401()
    {
        var a = await factory.RegistrarComercioAsync();
        var adulterado = ReemplazarClaim(a.Auth.AccessToken, PosVentasClaims.Rol, "SuperAdmin");
        using var cliente = factory.CrearClienteConToken(adulterado);

        var respuesta = await cliente.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_firmado_con_otra_clave_devuelve_401()
    {
        var a = await factory.RegistrarComercioAsync();
        var otraClave = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        var token = CrearToken(otraClave, a.Auth.UsuarioId, a.TenantId, "Administrador");
        using var cliente = factory.CrearClienteConToken(token);

        var respuesta = await cliente.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_sin_firma_alg_none_devuelve_401()
    {
        var a = await factory.RegistrarComercioAsync();
        var partes = a.Auth.AccessToken.Split('.');
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        using var cliente = factory.CrearClienteConToken($"{header}.{partes[1]}.");

        var respuesta = await cliente.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_valido_sin_claim_de_comercio_devuelve_403()
    {
        // Firmado con la clave correcta pero sin claim tid: autenticado, pero sin acceso a datos.
        var token = CrearToken(factory.ClaveFirmaJwt, Guid.NewGuid(), tenantId: null, "Administrador");
        using var cliente = factory.CrearClienteConToken(token);

        var respuesta = await cliente.GetAsync("/api/productos");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_devuelve_401_con_problem_details()
    {
        var a = await factory.RegistrarComercioAsync();
        using var anonimo = factory.CreateClient();

        var respuesta = await anonimo.PostAsJsonAsync("/api/auth/login",
            new { comercio = a.Slug, username = "admin", password = "incorrecta-123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_correcto_emite_token_con_uid_tid_y_role()
    {
        var a = await factory.RegistrarComercioAsync();
        using var anonimo = factory.CreateClient();

        var respuesta = await anonimo.PostAsJsonAsync("/api/auth/login",
            new { comercio = a.Slug, username = "ADMIN", password = a.Password });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonObject>();
        var jwt = new JsonWebToken(cuerpo!["accessToken"]!.GetValue<string>());

        Assert.Equal(a.TenantId.ToString(), jwt.GetClaim(PosVentasClaims.TenantId).Value);
        Assert.Equal(a.Auth.UsuarioId.ToString(), jwt.GetClaim(PosVentasClaims.UsuarioId).Value);
        Assert.Equal("Administrador", jwt.GetClaim(PosVentasClaims.Rol).Value);
    }

    private static string ReemplazarClaim(string token, string claim, string nuevoValor)
    {
        var partes = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(partes[1]))!.AsObject();
        payload[claim] = nuevoValor;
        return $"{partes[0]}.{Base64UrlEncoder.Encode(payload.ToJsonString())}.{partes[2]}";
    }

    private static string CrearToken(string clave, Guid usuarioId, Guid? tenantId, string rol)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = usuarioId.ToString(),
            [PosVentasClaims.UsuarioId] = usuarioId.ToString(),
            [PosVentasClaims.Rol] = rol
        };
        if (tenantId is { } tid)
            claims[PosVentasClaims.TenantId] = tid.ToString();

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)), SecurityAlgorithms.HmacSha256)
        });
    }
}
