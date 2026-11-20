using PosVentas.Application.Common.Interfaces;

namespace PosVentas.Infrastructure.Security;

/// <summary>
/// Hashing con BCrypt (sal aleatoria incluida en el hash, costo adaptativo).
/// Usa la variante "Enhanced" (pre-hash SHA-384) para no truncar contraseñas de más de 72 bytes.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int WorkFactor = 12;

    // Hash señuelo para igualar el tiempo de respuesta cuando el usuario no existe.
    private static readonly Lazy<string> HashSenuelo =
        new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString(), WorkFactor));

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);
    }

    public bool Verificar(string password, string? hash)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        if (string.IsNullOrEmpty(hash))
        {
            BCrypt.Net.BCrypt.EnhancedVerify(password, HashSenuelo.Value);
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
