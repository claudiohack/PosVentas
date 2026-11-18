namespace PosVentas.Application.Common.Security;

/// <summary>
/// Nombres de los claims del JWT de PosVentas (compartidos entre emisión y lectura).
/// </summary>
public static class PosVentasClaims
{
    public const string UsuarioId = "uid";
    public const string TenantId = "tid";
    public const string Rol = "role";
    public const string Nombre = "name";
}

/// <summary>
/// Nombres de las políticas de autorización.
/// </summary>
public static class Politicas
{
    /// <summary>Solo administradores del comercio.</summary>
    public const string SoloAdministrador = nameof(SoloAdministrador);

    /// <summary>Administradores y cajeros (operación de mostrador).</summary>
    public const string Operador = nameof(Operador);
}
