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
    /// <param name="rol">Rol del usuario: su nombre viaja como dato informativo (la autorización usa la base).</param>
    /// <param name="esAdministradorPlataforma">Agrega el claim de administrador de la plataforma (solo para el comercio operador).</param>
    TokenGenerado Generar(Usuario usuario, Rol rol, bool esAdministradorPlataforma = false);
}
