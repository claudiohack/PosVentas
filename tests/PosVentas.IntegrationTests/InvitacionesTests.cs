using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Auth;
using PosVentas.Infrastructure.Persistence;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class InvitacionesTests(PosVentasApiFactory factory)
{
    // a) El registro abierto está cerrado
    [Fact]
    public async Task Registro_anonimo_es_rechazado_con_401_y_un_comercio_comun_recibe_403()
    {
        using var anonimo = factory.CreateClient();
        var datos = DatosComercio(slug: $"anon-{Guid.NewGuid():N}"[..20], token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/auth/registro", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/auth/invitaciones", new { diasVigencia = 7 })).StatusCode);

        // Un administrador de un comercio cualquiera no es administrador de la plataforma
        var comercio = await factory.RegistrarComercioAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await comercio.Http.PostAsJsonAsync("/api/auth/registro", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await comercio.Http.PostAsJsonAsync("/api/auth/invitaciones", new { diasVigencia = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await comercio.Http.GetAsync("/api/auth/invitaciones")).StatusCode);

        // Al administrador de la plataforma el alta directa le indica el flujo de invitaciones
        using var plataforma = await factory.ClientePlataformaAsync();
        Assert.Equal(HttpStatusCode.Gone, (await plataforma.PostAsJsonAsync("/api/auth/registro", datos)).StatusCode);

        // Sin invitación, o con un token inventado, tampoco se registra
        Assert.Equal(HttpStatusCode.BadRequest, (await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", datos)).StatusCode);
        var inventado = DatosComercio($"inv-{Guid.NewGuid():N}"[..20], token: TokensInvitacion.Generar());
        Assert.Equal(HttpStatusCode.BadRequest, (await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", inventado)).StatusCode);
    }

    // b) Registro con invitación válida
    [Fact]
    public async Task Registro_con_invitacion_valida_crea_el_comercio_y_la_invitacion_no_se_puede_reusar()
    {
        using var plataforma = await factory.ClientePlataformaAsync();
        var emision = await plataforma.PostAsJsonAsync("/api/auth/invitaciones",
            new { emailDestinatario = "Dueno@Almacen.com.ar", nota = "Almacén de prueba", diasVigencia = 3 });
        Assert.Equal(HttpStatusCode.Created, emision.StatusCode);
        var invitacion = (await emision.Content.ReadFromJsonAsync<InvitacionCreadaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(43, invitacion.Token.Length);
        Assert.InRange(invitacion.VenceUtc, DateTime.UtcNow.AddDays(3).AddMinutes(-5), DateTime.UtcNow.AddDays(3).AddMinutes(5));

        using var anonimo = factory.CreateClient();
        var slug = $"alm-{Guid.NewGuid():N}"[..20];
        var registro = await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", DatosComercio(slug, invitacion.Token));

        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var auth = (await registro.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        var jwt = new JsonWebToken(auth.AccessToken);
        Assert.Equal(auth.ComercioId.ToString(), jwt.GetClaim(PosVentasClaims.TenantId).Value);
        Assert.False(jwt.TryGetClaim(PosVentasClaims.AdministradorPlataforma, out _)); // un comercio invitado no es plataforma
        Assert.False(auth.EsAdministradorPlataforma);

        // El comercio nuevo opera: tiene su caja y su categoría inicial
        using var nuevo = factory.CrearClienteConToken(auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.GetAsync("/api/cajas")).StatusCode);

        // La invitación quedó usada: reusarla falla aunque el slug sea otro
        var reuso = await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", DatosComercio($"otro-{Guid.NewGuid():N}"[..20], invitacion.Token));
        Assert.Equal(HttpStatusCode.BadRequest, reuso.StatusCode);

        var listado = (await plataforma.GetFromJsonAsync<List<InvitacionDto>>("/api/auth/invitaciones", ApiTestHelpers.Json))!;
        var usada = listado.Single(i => i.Id == invitacion.Id);
        Assert.Equal(("Usada", auth.ComercioId, "dueno@almacen.com.ar"), (usada.Estado, usada.ComercioCreadoId!.Value, usada.EmailDestinatario));

        // En la base solo queda el hash del token, nunca el token en claro
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guardada = await db.InvitacionesComercio.SingleAsync(i => i.Id == invitacion.Id);
        Assert.NotEqual(invitacion.Token, guardada.TokenHash);
        Assert.Equal(TokensInvitacion.Hash(invitacion.Token), guardada.TokenHash);
    }

    [Fact]
    public async Task Una_invitacion_revocada_no_permite_registrarse_y_el_admin_de_plataforma_tiene_su_claim()
    {
        using var plataforma = await factory.ClientePlataformaAsync();
        var invitacion = (await (await plataforma.PostAsJsonAsync("/api/auth/invitaciones", new { diasVigencia = 1 }))
            .Content.ReadFromJsonAsync<InvitacionCreadaDto>(ApiTestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.OK, (await plataforma.DeleteAsync($"/api/auth/invitaciones/{invitacion.Id}")).StatusCode);

        using var anonimo = factory.CreateClient();
        var registro = await anonimo.PostAsJsonAsync("/api/auth/registro-invitacion", DatosComercio($"rev-{Guid.NewGuid():N}"[..20], invitacion.Token));
        Assert.Equal(HttpStatusCode.BadRequest, registro.StatusCode);

        // Vigencia fuera de rango → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await plataforma.PostAsJsonAsync("/api/auth/invitaciones", new { diasVigencia = 90 })).StatusCode);

        // El login del comercio operador emite el claim de plataforma
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new
        {
            comercio = PosVentasApiFactory.SlugPlataforma, username = PosVentasApiFactory.UsuarioPlataforma, password = factory.PasswordPlataforma
        });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        Assert.True(auth.EsAdministradorPlataforma);
        Assert.Equal("1", new JsonWebToken(auth.AccessToken).GetClaim(PosVentasClaims.AdministradorPlataforma).Value);
    }

    private static object DatosComercio(string slug, string? token) => new
    {
        tokenInvitacion = token,
        nombreComercio = "Almacén invitado",
        slug,
        cuit = (string?)null,
        nombreAdministrador = "Dueño",
        username = "admin",
        password = "Clave-Segura-2026"
    };
}
