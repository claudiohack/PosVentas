namespace PosVentas.Application.Common.Security;

/// <summary>
/// Nombres de los claims del JWT de PosVentas (compartidos entre emisión y lectura).
/// Los permisos NO viajan en el token: se leen de la base en cada request (ver ValidacionSesionMiddleware),
/// así un cambio en un rol rige de inmediato y el token no crece con la cantidad de permisos.
/// </summary>
public static class PosVentasClaims
{
    public const string UsuarioId = "uid";
    public const string TenantId = "tid";

    /// <summary>Nombre visible del rol (informativo: la autorización usa los permisos del rol en la base).</summary>
    public const string Rol = "role";

    public const string Nombre = "name";

    /// <summary>Sello de seguridad del usuario: si cambió en la base, el token queda revocado.</summary>
    public const string SelloSeguridad = "sst";

    /// <summary>"1" solo en tokens de administradores del comercio operador de la plataforma (HS Tech).</summary>
    public const string AdministradorPlataforma = "plat";
}

/// <summary>Nombres de las políticas de autorización.</summary>
public static class Politicas
{
    /// <summary>Administradores de la plataforma (HS Tech): emiten invitaciones para dar de alta comercios.</summary>
    public const string AdministradorGlobal = nameof(AdministradorGlobal);

    /// <summary>
    /// Prefijo de las políticas por permiso: "perm:ventas.anular". Varios permisos separados por '|' significan
    /// "cualquiera de ellos" (por ejemplo, la lista de roles la usan tanto usuarios como roles).
    /// </summary>
    public const string PrefijoPermiso = "perm:";

    public static string DePermiso(params string[] permisos) => PrefijoPermiso + string.Join('|', permisos);
}

/// <summary>
/// Permiso que exige un comando o consulta, verificado por <c>AutorizacionBehavior</c> además de la política
/// del endpoint (defensa en profundidad: si un endpoint nuevo olvida su atributo, el caso de uso igual queda protegido).
/// Con varios atributos se exigen todos.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequierePermisoAttribute(string permiso) : Attribute
{
    public string Permiso { get; } = permiso;
}

/// <summary>Marca un comando que solo puede ejecutar un administrador de la plataforma (verificado por AutorizacionBehavior).</summary>
public interface IRequiereAdministradorPlataforma;
