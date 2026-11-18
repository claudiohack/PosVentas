namespace PosVentas.Application.Features.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiraUtc,
    Guid UsuarioId,
    string NombreCompleto,
    string Rol,
    Guid ComercioId,
    string Comercio);

internal static class AuthValidationRules
{
    public const string PatronSlug = "^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$";
    public const string PatronUsername = "^[a-zA-Z0-9._-]{3,50}$";
    public const int PasswordLongitudMinima = 10;
    public const int PasswordLongitudMaxima = 128;
}
