using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public enum RolUsuario
{
    Administrador = 1,
    Cajero = 2,
    Supervisor = 3
}

public sealed record UsuarioDto(
    Guid Id,
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    RolUsuario Rol,
    bool Activo,
    DateTime FechaCreacionUtc,
    DateTime? UltimoLoginUtc);

/// <summary>
/// Formulario del modal Nuevo / Editar usuario. La validación final la hace la API (FluentValidation);
/// estas reglas solo evitan viajes al servidor con datos obviamente inválidos.
/// </summary>
public sealed class UsuarioFormulario
{
    [Required(ErrorMessage = "Ingresá el nombre completo.")]
    [StringLength(150)]
    public string NombreCompleto { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá el nombre de usuario.")]
    [RegularExpression("^[a-zA-Z0-9._-]{3,50}$", ErrorMessage = "De 3 a 50 caracteres: letras, números, punto, guion o guion bajo.")]
    public string NombreUsuario { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "El email no es válido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    public RolUsuario Rol { get; set; } = RolUsuario.Cajero;

    /// <summary>Obligatoria en el alta; en la edición, vacía = no cambiar.</summary>
    [StringLength(128, MinimumLength = 10, ErrorMessage = "La contraseña debe tener entre 10 y 128 caracteres.")]
    public string? Password { get; set; }

    public static UsuarioFormulario Desde(UsuarioDto u) => new()
    {
        NombreCompleto = u.NombreCompleto,
        NombreUsuario = u.NombreUsuario,
        Email = u.Email,
        Rol = u.Rol
    };
}

public static class Roles
{
    public const string Administrador = nameof(RolUsuario.Administrador);
    public const string Supervisor = nameof(RolUsuario.Supervisor);

    public static string Descripcion(RolUsuario rol) => rol switch
    {
        RolUsuario.Administrador => "Gestiona usuarios, catálogo y crédito de clientes.",
        RolUsuario.Supervisor => "Vende, cobra y además cierra / arquea la caja.",
        RolUsuario.Cajero => "Vende, abre caja y registra cobros.",
        _ => string.Empty
    };
}
