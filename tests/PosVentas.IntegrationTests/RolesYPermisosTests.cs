using System.Net;
using System.Net.Http.Json;
using PosVentas.Application.Features.Auditoria;
using PosVentas.Application.Features.Auth;
using PosVentas.Application.Features.Roles;
using PosVentas.Domain.Seguridad;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class RolesYPermisosTests(PosVentasApiFactory factory)
{
    private const string Clave = "Usuario-2026-segura";

    [Fact]
    public async Task Comercio_nuevo_tiene_los_tres_roles_de_sistema_y_el_catalogo_de_permisos()
    {
        var a = await factory.RegistrarComercioAsync();

        Assert.True(a.Auth.EsAdministrador);
        Assert.Equal(Permisos.Todos.Order(), a.Auth.Permisos.Order());

        var roles = (await a.Http.GetFromJsonAsync<List<RolDto>>("/api/roles", ApiTestHelpers.Json))!;
        Assert.Equal(["Administrador", "Supervisor", "Cajero"], roles.Select(r => r.Nombre));
        Assert.All(roles, r => Assert.True(r.EsSistema));
        Assert.Equal(1, roles[0].CantidadUsuarios);
        Assert.Equal(Permisos.Todos.Count, roles[0].Permisos.Count);
        Assert.Equal(RolesSistema.PermisosSupervisor.Order(), roles[1].Permisos.Order());
        Assert.Equal(RolesSistema.PermisosCajero.Order(), roles[2].Permisos.Order());

        var catalogo = (await a.Http.GetFromJsonAsync<List<GrupoPermisosDto>>("/api/roles/permisos", ApiTestHelpers.Json))!;
        Assert.Equal(Permisos.Todos.Order(), catalogo.SelectMany(g => g.Permisos).Select(p => p.Codigo).Order());
    }

    [Fact]
    public async Task Rol_personalizado_otorga_solo_sus_permisos_y_los_cambios_rigen_sin_volver_a_ingresar()
    {
        var a = await factory.RegistrarComercioAsync();
        var categoria = await a.CategoriaGeneralAsync();
        var repositor = await CrearRolAsync(a, "Repositor", Permisos.ProductosConsultar, Permisos.ProductosGestionar);
        using var http = await CrearUsuarioYLoginAsync(a, "repo1", repositor.Id);

        Assert.Equal(HttpStatusCode.Created, (await (a with { Http = http }).CrearProductoAsync("REPO-1", categoria)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/ventas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/roles")).StatusCode);

        // El administrador le quita "gestionar productos" y le da "consultar ventas": rige en el próximo request, mismo token
        var edicion = await a.Http.PutAsJsonAsync($"/api/roles/{repositor.Id}", new
        {
            nombre = "Repositor", descripcion = "Repone y consulta", permisos = new[] { Permisos.ProductosConsultar, Permisos.VentasConsultar }
        }, ApiTestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, edicion.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await (a with { Http = http }).CrearProductoAsync("REPO-2", categoria)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/ventas")).StatusCode);

        var sesion = (await http.GetFromJsonAsync<SesionDto>("/api/auth/sesion", ApiTestHelpers.Json))!;
        Assert.Equal(("Repositor", false), (sesion.Rol, sesion.EsAdministrador));
        Assert.Equal(new[] { Permisos.VentasConsultar, Permisos.ProductosConsultar }.Order(), sesion.Permisos.Order());

        // También los roles de sistema son configurables: sin "ver reportes", el supervisor pierde el dashboard
        using var supervisor = await factory.LoginConRolAsync(a, "super1", "Supervisor");
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync("/api/reportes/dashboard")).StatusCode);
        var rolSupervisor = await a.RolIdAsync("Supervisor");
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync($"/api/roles/{rolSupervisor}", new
        {
            nombre = "Supervisor", descripcion = (string?)null,
            permisos = RolesSistema.PermisosSupervisor.Where(p => p != Permisos.ReportesVer).ToArray()
        }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.GetAsync("/api/reportes/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Administrador_es_inmutable_los_de_sistema_no_se_eliminan_y_un_rol_con_usuarios_tampoco()
    {
        var a = await factory.RegistrarComercioAsync();
        var admin = await a.RolIdAsync("Administrador");
        var supervisor = await a.RolIdAsync("Supervisor");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.PutAsJsonAsync($"/api/roles/{admin}",
            new { nombre = "Administrador", descripcion = (string?)null, permisos = new[] { Permisos.VentasRegistrar } }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.DeleteAsync($"/api/roles/{admin}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.DeleteAsync($"/api/roles/{supervisor}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.PutAsJsonAsync($"/api/roles/{supervisor}",
            new { nombre = "Jefe", descripcion = (string?)null, permisos = RolesSistema.PermisosSupervisor }, ApiTestHelpers.Json)).StatusCode);

        // Validaciones: nombre repetido (409), permiso inexistente o lista vacía (400)
        var deposito = await CrearRolAsync(a, "Depósito", Permisos.ProductosConsultar);
        Assert.Equal(HttpStatusCode.Conflict, (await PostRol(a, "depósito", Permisos.ProductosConsultar)).StatusCode); // sin distinguir mayúsculas
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRol(a, "Inventado", "permiso.inventado")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRol(a, "Vacío")).StatusCode);

        // Con usuarios no se elimina (409); reasignado, sí; y ya no se puede asignar
        var usuario = await CrearUsuarioAsync(a, "depo1", deposito.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Http.DeleteAsync($"/api/roles/{deposito.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync($"/api/usuarios/{usuario}",
            new { nombreCompleto = "depo1", email = (string?)null, rolId = await a.RolIdAsync("Cajero") }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Http.DeleteAsync($"/api/roles/{deposito.Id}")).StatusCode);

        Assert.DoesNotContain((await a.Http.GetFromJsonAsync<List<RolDto>>("/api/roles", ApiTestHelpers.Json))!, r => r.Id == deposito.Id);
        Assert.Contains((await a.Http.GetFromJsonAsync<List<RolDto>>("/api/roles?incluirEliminados=true", ApiTestHelpers.Json))!, r => r.Id == deposito.Id && !r.Activo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostUsuario(a, "depo2", deposito.Id)).StatusCode);

        // Un rol eliminado libera su nombre
        Assert.Equal(HttpStatusCode.Created, (await PostRol(a, "Depósito", Permisos.ProductosConsultar)).StatusCode);
    }

    [Fact]
    public async Task Sin_escalada_de_privilegios_nadie_otorga_ni_administra_permisos_que_no_tiene()
    {
        var a = await factory.RegistrarComercioAsync();
        var encargado = await CrearRolAsync(a, "Encargado",
            Permisos.UsuariosGestionar, Permisos.RolesGestionar, Permisos.ProductosConsultar, Permisos.VentasConsultar);
        using var http = await CrearUsuarioYLoginAsync(a, "encargado1", encargado.Id);
        var comoEncargado = a with { Http = http };

        // Roles: solo con permisos propios
        Assert.Equal(HttpStatusCode.Forbidden, (await PostRol(comoEncargado, "Anulador", Permisos.VentasAnular)).StatusCode);
        var consulta = await CrearRolAsync(comoEncargado, "Consulta", Permisos.ProductosConsultar, Permisos.VentasConsultar);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/roles/{encargado.Id}", new
        {
            nombre = "Encargado", descripcion = (string?)null,
            permisos = new[] { Permisos.UsuariosGestionar, Permisos.RolesGestionar, Permisos.ProductosConsultar, Permisos.VentasConsultar, Permisos.ReportesVer }
        }, ApiTestHelpers.Json)).StatusCode);
        // El Supervisor tiene permisos que el encargado no: no lo puede tocar
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/roles/{await a.RolIdAsync("Supervisor")}",
            new { nombre = "Supervisor", descripcion = (string?)null, permisos = new[] { Permisos.ProductosConsultar } }, ApiTestHelpers.Json)).StatusCode);

        // Usuarios: no asigna el Administrador ni roles con más permisos (Cajero vende); sí uno propio
        Assert.Equal(HttpStatusCode.Forbidden, (await PostUsuario(comoEncargado, "nuevoadmin", await a.RolIdAsync("Administrador"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostUsuario(comoEncargado, "nuevocajero", await a.RolIdAsync("Cajero"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostUsuario(comoEncargado, "consultor1", consulta.Id)).StatusCode);

        // Ni resetea la clave ni da de baja al Administrador
        var adminId = a.Auth.UsuarioId;
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/usuarios/{adminId}/password",
            new { nuevaPassword = "Tomada-12345678" }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PatchAsJsonAsync($"/api/usuarios/{adminId}/estado",
            new { activo = false }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/usuarios/{adminId}",
            new { nombreCompleto = "Admin", email = (string?)null, rolId = consulta.Id }, ApiTestHelpers.Json)).StatusCode);

        // El administrador sigue entrando con su clave
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonimo.PostAsJsonAsync("/api/auth/login",
            new { comercio = a.Slug, username = "admin", password = a.Password })).StatusCode);
    }

    [Fact]
    public async Task Los_roles_estan_aislados_por_comercio()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var rolA = await CrearRolAsync(a, "Exclusivo A", Permisos.ProductosConsultar);

        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/roles/{rolA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"/api/roles/{rolA.Id}",
            new { nombre = "Robado", descripcion = (string?)null, permisos = new[] { Permisos.ProductosConsultar } }, ApiTestHelpers.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.DeleteAsync($"/api/roles/{rolA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostUsuario(b, "intruso", rolA.Id)).StatusCode);
        Assert.DoesNotContain((await b.Http.GetFromJsonAsync<List<RolDto>>("/api/roles", ApiTestHelpers.Json))!, r => r.Id == rolA.Id);

        // Mismo nombre en otro comercio: permitido
        Assert.Equal(HttpStatusCode.Created, (await PostRol(b, "Exclusivo A", Permisos.ProductosConsultar)).StatusCode);
    }

    [Fact]
    public async Task La_auditoria_registra_cambios_de_roles_y_usuarios_por_comercio()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var rol = await CrearRolAsync(a, "Cobrador", Permisos.ClientesConsultar);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync($"/api/roles/{rol.Id}",
            new { nombre = "Cobrador", descripcion = (string?)null, permisos = new[] { Permisos.ClientesConsultar, Permisos.ClientesCobrar } }, ApiTestHelpers.Json)).StatusCode);
        var usuario = await CrearUsuarioAsync(a, "cobra1", rol.Id);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync($"/api/usuarios/{usuario}",
            new { nombreCompleto = "cobra1", email = (string?)null, rolId = await a.RolIdAsync("Cajero") }, ApiTestHelpers.Json)).StatusCode);

        var pagina = (await a.Http.GetFromJsonAsync<PaginaAuditoriaDto>("/api/auditoria", ApiTestHelpers.Json))!;
        Assert.Equal(["usuario.rol-cambiado", "usuario.creado", "rol.modificado", "rol.creado"], pagina.Registros.Select(r => r.Accion));
        Assert.Contains("Cobrar cuentas corrientes", pagina.Registros.Single(r => r.Accion == "rol.modificado").Detalle);
        Assert.All(pagina.Registros, r => Assert.Equal(a.Auth.UsuarioId, r.UsuarioId));

        var deRol = (await a.Http.GetFromJsonAsync<PaginaAuditoriaDto>($"/api/auditoria?entidad=Rol&entidadId={rol.Id}", ApiTestHelpers.Json))!;
        Assert.Equal(2, deRol.Total);

        // Otro comercio no ve nada; un cajero no consulta la auditoría
        Assert.Equal(0, (await b.Http.GetFromJsonAsync<PaginaAuditoriaDto>("/api/auditoria", ApiTestHelpers.Json))!.Total);
        using var cajero = await factory.LoginConRolAsync(a, "cajero-aud", "Cajero");
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/auditoria")).StatusCode);
    }

    // ---------- Helpers ----------

    private static Task<HttpResponseMessage> PostRol(ComercioDePrueba comercio, string nombre, params string[] permisos) =>
        comercio.Http.PostAsJsonAsync("/api/roles", new { nombre, descripcion = (string?)null, permisos }, ApiTestHelpers.Json);

    private static async Task<RolDto> CrearRolAsync(ComercioDePrueba comercio, string nombre, params string[] permisos)
    {
        var respuesta = await PostRol(comercio, nombre, permisos);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<RolDto>(ApiTestHelpers.Json))!;
    }

    private static Task<HttpResponseMessage> PostUsuario(ComercioDePrueba comercio, string usuario, Guid rolId) =>
        comercio.Http.PostAsJsonAsync("/api/usuarios",
            new { nombreCompleto = usuario, nombreUsuario = usuario, email = (string?)null, password = Clave, rolId }, ApiTestHelpers.Json);

    private static async Task<Guid> CrearUsuarioAsync(ComercioDePrueba comercio, string usuario, Guid rolId)
    {
        var respuesta = await PostUsuario(comercio, usuario, rolId);
        Assert.True(respuesta.StatusCode == HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<Application.Features.Usuarios.UsuarioDto>(ApiTestHelpers.Json))!.Id;
    }

    private async Task<HttpClient> CrearUsuarioYLoginAsync(ComercioDePrueba comercio, string usuario, Guid rolId)
    {
        await CrearUsuarioAsync(comercio, usuario, rolId);
        using var anonimo = factory.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new { comercio = comercio.Slug, username = usuario, password = Clave });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestHelpers.Json))!;
        Assert.False(auth.EsAdministrador);
        return factory.CrearClienteConToken(auth.AccessToken);
    }
}
