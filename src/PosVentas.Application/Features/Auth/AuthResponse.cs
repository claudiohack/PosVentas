using FluentValidation;

namespace PosVentas.Application.Features.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiraUtc,
    Guid UsuarioId,
    string NombreCompleto,
    string Rol,
    Guid ComercioId,
    string Comercio,
    bool EsAdministradorPlataforma = false);

internal static class AuthValidationRules
{
    public const string PatronSlug = "^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$";
    public const string PatronUsername = "^[a-zA-Z0-9._-]{3,50}$";
    public const int PasswordLongitudMinima = 10;
    public const int PasswordLongitudMaxima = 128;

    /// <summary>Complejidad mínima de contraseña: 10 a 128 caracteres, con letras y números.</summary>
    public static IRuleBuilderOptions<T, string> PasswordSegura<T>(this IRuleBuilder<T, string> regla) =>
        regla.NotEmpty()
            .MinimumLength(PasswordLongitudMinima)
            .MaximumLength(PasswordLongitudMaxima)
            .Must(p => p is not null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("La contraseña debe tener al menos 10 caracteres e incluir letras y números.");
}
