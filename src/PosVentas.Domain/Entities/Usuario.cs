using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Usuario de un comercio. El hash de la contraseña lo calcula la infraestructura (BCrypt):
/// el dominio nunca ve contraseñas en texto plano.
/// </summary>
public sealed class Usuario : TenantEntity
{
    public const int LongitudMaximaUsername = 50;
    public const int LongitudMaximaNombre = 150;

    public string NombreCompleto { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public RolUsuario Rol { get; private set; }
    public bool EstaActivo { get; private set; }
    public DateTime? UltimoLoginUtc { get; private set; }

    private Usuario(Guid id, string nombreCompleto, string username, string passwordHash, RolUsuario rol) : base(id)
    {
        NombreCompleto = nombreCompleto;
        Username = username;
        PasswordHash = passwordHash;
        Rol = rol;
        EstaActivo = true;
    }

    // Requerido por EF Core
    private Usuario() { }

    public static Usuario Crear(string nombreCompleto, string username, string passwordHash, RolUsuario rol)
    {
        if (string.IsNullOrWhiteSpace(nombreCompleto))
            throw new DomainException("El nombre completo es obligatorio.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("El hash de la contraseña es obligatorio.");
        if (!Enum.IsDefined(rol))
            throw new DomainException("El rol indicado no es válido.");

        return new Usuario(Guid.NewGuid(), nombreCompleto.Trim(), NormalizarUsername(username), passwordHash, rol);
    }

    public static string NormalizarUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("El nombre de usuario es obligatorio.");
        return username.Trim().ToLowerInvariant();
    }

    public void CambiarPasswordHash(string nuevoHash)
    {
        if (string.IsNullOrWhiteSpace(nuevoHash))
            throw new DomainException("El hash de la contraseña es obligatorio.");
        PasswordHash = nuevoHash;
        MarcarModificado();
    }

    public void CambiarRol(RolUsuario rol)
    {
        if (!Enum.IsDefined(rol))
            throw new DomainException("El rol indicado no es válido.");
        Rol = rol;
        MarcarModificado();
    }

    public void RegistrarLogin() => UltimoLoginUtc = DateTime.UtcNow;

    public void Desactivar()
    {
        EstaActivo = false;
        MarcarModificado();
    }

    public void Activar()
    {
        EstaActivo = true;
        MarcarModificado();
    }
}
