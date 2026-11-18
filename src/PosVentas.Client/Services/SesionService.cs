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
