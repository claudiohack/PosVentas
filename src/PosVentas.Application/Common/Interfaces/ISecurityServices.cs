using PosVentas.Domain.Entities;

namespace PosVentas.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifica la contraseña. Si <paramref name="hash"/> es null igual consume el mismo tiempo
    /// (evita enumerar usuarios por diferencia de tiempos) y devuelve false.
    /// </summary>
    bool Verificar(string password, string? hash);
}

public sealed record TokenGenerado(string AccessToken, DateTime ExpiraUtc);

public interface IJwtTokenGenerator
{
    TokenGenerado Generar(Usuario usuario);
}
