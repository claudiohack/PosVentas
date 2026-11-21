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

    /// <summary>Sello de seguridad del usuario: si cambió en la base, el token queda revocado.</summary>
    public const string SelloSeguridad = "sst";

    /// <summary>"1" solo en tokens de administradores del comercio operador de la plataforma (HS Tech).</summary>
    public const string AdministradorPlataforma = "plat";
}

/// <summary>
/// Nombres de las políticas de autorización.
/// </summary>
public static class Politicas
{
    /// <summary>Solo administradores del comercio.</summary>
    public const string SoloAdministrador = nameof(SoloAdministrador);

    /// <summary>Administradores y supervisores (cierre y arqueo de caja).</summary>
    public const string Supervision = nameof(Supervision);

    /// <summary>Todos los roles operativos: administrador, supervisor y cajero (mostrador).</summary>
    public const string Operador = nameof(Operador);

    /// <summary>Administradores de la plataforma (HS Tech): emiten invitaciones para dar de alta comercios.</summary>
    public const string AdministradorGlobal = nameof(AdministradorGlobal);
}

/// <summary>Marca un comando que solo puede ejecutar un administrador de la plataforma (verificado por AutorizacionBehavior).</summary>
public interface IRequiereAdministradorPlataforma;

/// <summary>
/// Marca un comando o consulta que solo puede ejecutar un Administrador.
/// Lo verifica <c>AutorizacionBehavior</c> además de la política del endpoint (defensa en profundidad).
/// </summary>
public interface IRequiereAdministrador;
