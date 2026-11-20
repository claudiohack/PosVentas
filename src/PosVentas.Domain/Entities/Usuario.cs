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
    public const int LongitudMaximaEmail = 150;

    public string NombreCompleto { get; private set; } = string.Empty;

    /// <summary>Nombre de usuario (login), normalizado a minúsculas. Único por comercio.</summary>
    public string Username { get; private set; } = string.Empty;

    public string? Email { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public RolUsuario Rol { get; private set; }
    public bool EstaActivo { get; private set; }
    public DateTime? UltimoLoginUtc { get; private set; }

    /// <summary>
    /// Sello de seguridad: viaja en el JWT y cambia al modificar contraseña, rol o estado.
    /// Un token con un sello viejo deja de ser válido aunque no haya expirado.
    /// </summary>
    public Guid SecurityStamp { get; private set; }

    private Usuario(Guid id, string nombreCompleto, string username, string? email, string passwordHash, RolUsuario rol) : base(id)
    {
        NombreCompleto = nombreCompleto;
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
        Rol = rol;
        EstaActivo = true;
        SecurityStamp = Guid.NewGuid();
    }

    // Requerido por EF Core
    private Usuario() { }

    public static Usuario Crear(string nombreCompleto, string username, string passwordHash, RolUsuario rol, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("El hash de la contraseña es obligatorio.");
        ValidarRol(rol);

        return new Usuario(Guid.NewGuid(), NormalizarNombre(nombreCompleto), NormalizarUsername(username),
            NormalizarEmail(email), passwordHash, rol);
    }

    public static string NormalizarUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("El nombre de usuario es obligatorio.");
        return username.Trim().ToLowerInvariant();
    }

    public void ActualizarDatos(string nombreCompleto, string? email)
    {
        NombreCompleto = NormalizarNombre(nombreCompleto);
        Email = NormalizarEmail(email);
        MarcarModificado();
    }

    public void CambiarPasswordHash(string nuevoHash)
    {
        if (string.IsNullOrWhiteSpace(nuevoHash))
            throw new DomainException("El hash de la contraseña es obligatorio.");
        PasswordHash = nuevoHash;
        RenovarSello();
    }

    public void CambiarRol(RolUsuario rol)
    {
        ValidarRol(rol);
        if (Rol == rol)
            return;
        Rol = rol;
        RenovarSello();
    }

    public void RegistrarLogin() => UltimoLoginUtc = DateTime.UtcNow;

    /// <param name="solicitanteId">Usuario que ejecuta la baja (no puede ser el mismo).</param>
    public void Desactivar(Guid solicitanteId)
    {
        if (solicitanteId == Id)
            throw new DomainException("Un usuario no puede desactivarse a sí mismo.");
        if (!EstaActivo)
            return;
        EstaActivo = false;
        RenovarSello();
    }

    public void Activar()
    {
        if (EstaActivo)
            return;
        EstaActivo = true;
        RenovarSello();
    }

    private void RenovarSello()
    {
        SecurityStamp = Guid.NewGuid();
        MarcarModificado();
    }

    private static void ValidarRol(RolUsuario rol)
    {
        if (!Enum.IsDefined(rol))
            throw new DomainException("El rol indicado no es válido.");
    }

    private static string NormalizarNombre(string? nombreCompleto)
    {
        if (string.IsNullOrWhiteSpace(nombreCompleto))
            throw new DomainException("El nombre completo es obligatorio.");
        return nombreCompleto.Trim();
    }

    private static string? NormalizarEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
