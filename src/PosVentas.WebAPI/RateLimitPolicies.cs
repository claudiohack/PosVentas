namespace PosVentas.WebAPI;

public static class RateLimitPolicies
{
    /// <summary>Límite por IP para login y alta de comercio (fuerza bruta / abuso).</summary>
    public const string Auth = "auth";
}
