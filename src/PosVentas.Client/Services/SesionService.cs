using PosVentas.Client.Models;

namespace PosVentas.Client.Services;

/// <summary>
/// Sesión del usuario en memoria (el token no se persiste en localStorage para no exponerlo a XSS).
/// Al recargar la página hay que volver a iniciar sesión.
/// </summary>
public sealed class SesionService
{
    public AuthResponse? Actual { get; private set; }

    public bool EstaAutenticado => Actual is not null && Actual.ExpiraUtc > DateTime.UtcNow;

    // Solo controlan qué se muestra: la API vuelve a verificar el rol en cada request.
    public bool EsAdministrador => EstaAutenticado && Actual!.Rol == Roles.Administrador;

    /// <summary>Administrador del comercio operador (HS Tech): emite invitaciones para comercios nuevos.</summary>
    public bool EsAdministradorPlataforma => EstaAutenticado && Actual!.EsAdministradorPlataforma;

    /// <summary>Administrador o Supervisor: puede cerrar y arquear la caja.</summary>
    public bool PuedeSupervisar => EstaAutenticado && Actual!.Rol is Roles.Administrador or Roles.Supervisor;

    public event Action? Cambio;

    public void Iniciar(AuthResponse respuesta)
    {
        Actual = respuesta;
        Cambio?.Invoke();
    }

    public void Cerrar()
    {
        Actual = null;
        Cambio?.Invoke();
    }
}
