using PosVentas.Client.Models;

namespace PosVentas.Client.Services;

/// <summary>
/// Sesión del usuario en memoria (el token no se persiste en localStorage para no exponerlo a XSS).
/// Al recargar la página hay que volver a iniciar sesión. Los permisos se refrescan desde la API
/// (GET /api/auth/sesion) al navegar, así un cambio en el rol se refleja en el menú sin reingresar.
/// </summary>
public sealed class SesionService
{
    private HashSet<string> _permisos = [];

    public AuthResponse? Actual { get; private set; }

    public bool EstaAutenticado => Actual is not null && Actual.ExpiraUtc > DateTime.UtcNow;

    // Solo controlan qué se muestra: la API vuelve a verificar los permisos en cada request.
    public bool EsAdministrador => EstaAutenticado && Actual!.EsAdministrador;

    /// <summary>Administrador del comercio operador (HS Tech): emite invitaciones para comercios nuevos.</summary>
    public bool EsAdministradorPlataforma => EstaAutenticado && Actual!.EsAdministradorPlataforma;

    public bool Tiene(string permiso) => EstaAutenticado && (Actual!.EsAdministrador || _permisos.Contains(permiso));

    public bool TieneAlguno(params string[] permisos) => permisos.Any(Tiene);

    /// <summary>Primera pantalla a la que el rol tiene acceso (un rol sin "vender" no aterriza en el mostrador).</summary>
    public string PaginaInicial() =>
        Tiene(Permisos.VentasRegistrar) ? "/pos"
        : Tiene(Permisos.CajaOperar) ? "/caja"
        : Tiene(Permisos.ProductosConsultar) ? "/productos"
        : Tiene(Permisos.ReportesVer) ? "/dashboard"
        : Tiene(Permisos.VentasConsultar) ? "/ventas"
        : Tiene(Permisos.ClientesConsultar) ? "/clientes"
        : Tiene(Permisos.UsuariosGestionar) ? "/usuarios"
        : "/sin-acceso";

    /// <summary>Momento del último refresco de permisos (para no consultar la API en cada clic).</summary>
    public DateTime UltimaActualizacionUtc { get; private set; }

    public event Action? Cambio;

    public void Iniciar(AuthResponse respuesta)
    {
        Actual = respuesta;
        _permisos = [.. respuesta.Permisos];
        UltimaActualizacionUtc = DateTime.UtcNow;
        Cambio?.Invoke();
    }

    /// <summary>Aplica rol y permisos vigentes según la API. Solo notifica si algo cambió.</summary>
    public void Actualizar(SesionDto sesion)
    {
        UltimaActualizacionUtc = DateTime.UtcNow;
        if (Actual is null || sesion.UsuarioId != Actual.UsuarioId)
            return;

        var cambio = sesion.RolId != Actual.RolId || sesion.Rol != Actual.Rol || sesion.EsAdministrador != Actual.EsAdministrador
                     || !_permisos.SetEquals(sesion.Permisos);
        if (!cambio)
            return;

        Actual = Actual with { RolId = sesion.RolId, Rol = sesion.Rol, EsAdministrador = sesion.EsAdministrador, Permisos = sesion.Permisos };
        _permisos = [.. sesion.Permisos];
        Cambio?.Invoke();
    }

    public void Cerrar()
    {
        Actual = null;
        _permisos = [];
        Cambio?.Invoke();
    }
}
