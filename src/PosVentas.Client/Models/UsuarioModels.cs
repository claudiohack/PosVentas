using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public sealed record UsuarioDto(
    Guid Id,
    string NombreCompleto,
    string NombreUsuario,
    string? Email,
    Guid RolId,
    string Rol,
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

    public Guid RolId { get; set; }

    /// <summary>Obligatoria en el alta; en la edición, vacía = no cambiar.</summary>
    [StringLength(128, MinimumLength = 10, ErrorMessage = "La contraseña debe tener entre 10 y 128 caracteres.")]
    public string? Password { get; set; }

    public static UsuarioFormulario Desde(UsuarioDto u) => new()
    {
        NombreCompleto = u.NombreCompleto,
        NombreUsuario = u.NombreUsuario,
        Email = u.Email,
        RolId = u.RolId
    };
}
