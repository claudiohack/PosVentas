using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Usuarios;
using PosVentas.Domain.Enums;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class UsuariosYRolesTests(PosVentasApiFactory factory)
{
    private const string PasswordCajero = "Cajero-2026-segura";

    // a) Alta exitosa
    [Fact]
    public async Task Administrador_crea_usuario_cajero1_y_recibe_201()
    {
        var a = await factory.RegistrarComercioAsync();

        var respuesta = await CrearUsuarioAsync(a, "cajero1", RolUsuario.Cajero);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.NotNull(respuesta.Headers.Location);
        var usuario = (await respuesta.Content.ReadFromJsonAsync<UsuarioDto>(ApiTestHelpers.Json))!;
        Assert.Equal("cajero1", usuario.NombreUsuario);
        Assert.Equal(RolUsuario.Cajero, usuario.Rol);
        Assert.True(usuario.Activo);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", cuerpo, StringComparison.OrdinalIgnoreCase); // nunca se expone el hash

        var listado = await a.Http.GetFromJsonAsync<List<UsuarioDto>>("/api/usuarios", ApiTestHelpers.Json);
        Assert.Contains(listado!, u => u.Id == usuario.Id);
    }

    // b) Login de cajero
    [Fact]
    public async Task Cajero1_inicia_sesion_y_obtiene_JWT_con_rol_Cajero_y_su_TenantId()
    {
        var a = await factory.RegistrarComercioAsync();
        var creado = await CrearUsuarioOkAsync(a, "cajero1", RolUsuario.Cajero);

        using var anonimo = factory.CreateClient();
        var respuesta = await anonimo.PostAsJsonAsync("/api/auth/login",
            new { comercio = a.Slug, username = "cajero1", password = PasswordCajero });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var auth = (await respuesta.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        var jwt = new JsonWebToken(auth.AccessToken);
        Assert.Equal("Cajero", jwt.GetClaim(PosVentasClaims.Rol).Value);
        Assert.Equal(a.TenantId.ToString(), jwt.GetClaim(PosVentasClaims.TenantId).Value);
        Assert.Equal(creado.Id.ToString(), jwt.GetClaim(PosVentasClaims.UsuarioId).Value);

        // El token sirve para operar el mostrador
        using var cajero = factory.CrearClienteConToken(auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await cajero.GetAsync("/api/productos")).StatusCode);
    }

    // c) Restricción de rol
    [Fact]
    public async Task Cajero_no_puede_listar_ver_ni_crear_usuarios_403()
    {
        var a = await factory.RegistrarComercioAsync();
        using var cajero = await LoginComoAsync(a, "cajero1", RolUsuario.Cajero);

        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync($"/api/usuarios/{a.Auth.UsuarioId}")).StatusCode);

        var alta = await cajero.PostAsJsonAsync("/api/usuarios", NuevoUsuario("cajero2", RolUsuario.Administrador), ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, alta.StatusCode);

        var estado = await cajero.PatchAsJsonAsync($"/api/usuarios/{a.Auth.UsuarioId}/estado", new { activo = false }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, estado.StatusCode);

        var usuarios = await a.Http.GetFromJsonAsync<List<UsuarioDto>>("/api/usuarios", ApiTestHelpers.Json);
        Assert.DoesNotContain(usuarios!, u => u.NombreUsuario == "cajero2");
    }

    // d) Duplicado en el mismo comercio
    [Fact]
    public async Task Crear_dos_veces_cajero1_en_el_mismo_comercio_devuelve_409()
    {
        var a = await factory.RegistrarComercioAsync();
        await CrearUsuarioOkAsync(a, "cajero1", RolUsuario.Cajero);

        var repetido = await CrearUsuarioAsync(a, "CAJERO1", RolUsuario.Supervisor); // no distingue mayúsculas

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("application/problem+json", repetido.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Dos_comercios_pueden_tener_su_propio_cajero1()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();

        Assert.Equal(HttpStatusCode.Created, (await CrearUsuarioAsync(a, "cajero1", RolUsuario.Cajero)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearUsuarioAsync(b, "cajero1", RolUsuario.Cajero)).StatusCode);
    }

    // e) Aislamiento multi-tenant
    [Fact]
    public async Task Comercio_B_recibe_404_al_ver_o_modificar_usuarios_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var cajeroA = await CrearUsuarioOkAsync(a, "cajero1", RolUsuario.Cajero);
        var ruta = $"/api/usuarios/{cajeroA.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync(ruta,
            new { nombreCompleto = "Hackeado", email = (string?)null, rol = "Administrador" }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"{ruta}/password",
            new { nuevaPassword = "Robada-123456789" }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PatchAsJsonAsync($"{ruta}/estado",
            new { activo = false }, ApiTestHelpers.Json)).StatusCode);

        var listadoB = await b.Http.GetFromJsonAsync<List<UsuarioDto>>("/api/usuarios", ApiTestHelpers.Json);
        Assert.DoesNotContain(listadoB!, u => u.Id == cajeroA.Id);

        // A sigue intacto y su cajero puede entrar con la clave original
        var intacto = await a.Http.GetFromJsonAsync<UsuarioDto>(ruta, ApiTestHelpers.Json);
        Assert.Equal(RolUsuario.Cajero, intacto!.Rol);
        Assert.True(intacto.Activo);
        using var login = await LoginAsync(a.Slug, "cajero1", PasswordCajero);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ---------- Reglas de negocio y revocación ----------

    [Fact]
    public async Task Un_usuario_no_puede_desactivarse_a_si_mismo_422()
    {
        var a = await factory.RegistrarComercioAsync();

        var respuesta = await a.Http.PatchAsJsonAsync($"/api/usuarios/{a.Auth.UsuarioId}/estado", new { activo = false }, ApiTestHelpers.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_quitar_el_rol_al_unico_administrador_422()
    {
        var a = await factory.RegistrarComercioAsync();

        var respuesta = await a.Http.PutAsJsonAsync($"/api/usuarios/{a.Auth.UsuarioId}",
            new { nombreCompleto = "Admin", email = (string?)null, rol = "Cajero" }, ApiTestHelpers.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
    }

    [Fact]
    public async Task Al_desactivar_un_cajero_su_token_vigente_deja_de_servir_y_no_puede_loguearse()
    {
        var a = await factory.RegistrarComercioAsync();
        var cajero = await CrearUsuarioOkAsync(a, "cajero1", RolUsuario.Cajero);
        using var sesionCajero = await LoginComoExistenteAsync(a, "cajero1");
        Assert.Equal(HttpStatusCode.OK, (await sesionCajero.GetAsync("/api/productos")).StatusCode);

        var baja = await a.Http.PatchAsJsonAsync($"/api/usuarios/{cajero.Id}/estado", new { activo = false }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await sesionCajero.GetAsync("/api/productos")).StatusCode);
        using var login = await LoginAsync(a.Slug, "cajero1", PasswordCajero);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        // Reactivado vuelve a entrar
        await a.Http.PatchAsJsonAsync($"/api/usuarios/{cajero.Id}/estado", new { activo = true }, ApiTestHelpers.Json);
        using var login2 = await LoginAsync(a.Slug, "cajero1", PasswordCajero);
        Assert.Equal(HttpStatusCode.OK, login2.StatusCode);
    }

    [Fact]
    public async Task Reset_de_password_revoca_el_token_anterior_y_habilita_la_clave_nueva()
    {
        var a = await factory.RegistrarComercioAsync();
        var cajero = await CrearUsuarioOkAsync(a, "cajero1", RolUsuario.Cajero);
        using var sesionVieja = await LoginComoExistenteAsync(a, "cajero1");

        var debil = await a.Http.PutAsJsonAsync($"/api/usuarios/{cajero.Id}/password", new { nuevaPassword = "corta" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.BadRequest, debil.StatusCode);

        var reset = await a.Http.PutAsJsonAsync($"/api/usuarios/{cajero.Id}/password", new { nuevaPassword = "Nueva-Clave-2026" }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await sesionVieja.GetAsync("/api/productos")).StatusCode);
        using var conVieja = await LoginAsync(a.Slug, "cajero1", PasswordCajero);
        Assert.Equal(HttpStatusCode.Unauthorized, conVieja.StatusCode);
        using var conNueva = await LoginAsync(a.Slug, "cajero1", "Nueva-Clave-2026");
        Assert.Equal(HttpStatusCode.OK, conNueva.StatusCode);
    }

    [Fact]
    public async Task Cajero_vende_pero_no_cierra_caja_y_un_supervisor_si()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("SKU-ROL", precioCompra: 100m, ganancia: 0m, iva: 0m, stockInicial: 5m);
        var sesion = await a.AbrirCajaAsync(0m);

        using var cajero = await LoginComoAsync(a, "cajero1", RolUsuario.Cajero);
        var comoCajero = a with { Http = cajero };
        var venta = await comoCajero.VenderAsync(sesion.Id, null, [(producto.Id, 1m)], [("Efectivo", 100m)]);
        Assert.Equal(HttpStatusCode.Created, venta.StatusCode);

        var cierreCajero = await cajero.PostAsJsonAsync($"/api/cajas/sesiones/{sesion.Id}/cierre", new { montoCierreReal = 100m });
        Assert.Equal(HttpStatusCode.Forbidden, cierreCajero.StatusCode);

        using var supervisor = await LoginComoAsync(a, "super1", RolUsuario.Supervisor);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.GetAsync("/api/usuarios")).StatusCode);
        var cierre = await supervisor.PostAsJsonAsync($"/api/cajas/sesiones/{sesion.Id}/cierre", new { montoCierreReal = 100m });
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
    }

    // ---------- Helpers ----------

    private static object NuevoUsuario(string nombreUsuario, RolUsuario rol) => new
    {
        nombreCompleto = $"Usuario {nombreUsuario}",
        nombreUsuario,
        email = $"{nombreUsuario}@ejemplo.com",
        password = PasswordCajero,
        rol = rol.ToString()
    };

    private static Task<HttpResponseMessage> CrearUsuarioAsync(ComercioDePrueba comercio, string nombreUsuario, RolUsuario rol) =>
        comercio.Http.PostAsJsonAsync("/api/usuarios", NuevoUsuario(nombreUsuario, rol), ApiTestHelpers.Json);

    private static async Task<UsuarioDto> CrearUsuarioOkAsync(ComercioDePrueba comercio, string nombreUsuario, RolUsuario rol)
    {
        var respuesta = await CrearUsuarioAsync(comercio, nombreUsuario, rol);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<UsuarioDto>(ApiTestHelpers.Json))!;
    }

    private async Task<HttpResponseMessage> LoginAsync(string slug, string username, string password)
    {
        using var anonimo = factory.CreateClient();
        return await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = slug, username, password });
    }

    private async Task<HttpClient> LoginComoExistenteAsync(ComercioDePrueba comercio, string username)
    {
        using var respuesta = await LoginAsync(comercio.Slug, username, PasswordCajero);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var auth = (await respuesta.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        return factory.CrearClienteConToken(auth.AccessToken);
    }

    private async Task<HttpClient> LoginComoAsync(ComercioDePrueba comercio, string username, RolUsuario rol)
    {
        await CrearUsuarioOkAsync(comercio, username, rol);
        return await LoginComoExistenteAsync(comercio, username);
    }
}
