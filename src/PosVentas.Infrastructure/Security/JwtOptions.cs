namespace PosVentas.Infrastructure.Security;

/// <summary>
/// Configuración del JWT. <see cref="SigningKey"/> NUNCA va en appsettings:
/// en desarrollo se carga desde User Secrets y en producción desde la variable de entorno <c>Jwt__SigningKey</c>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int LongitudMinimaClave = 32;

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int ExpiracionMinutos { get; init; } = 480;
}
