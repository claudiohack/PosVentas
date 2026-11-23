using PosVentas.Domain.Common;
using PosVentas.Domain.Seguridad;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Rol de un comercio: un nombre y un conjunto de permisos del catálogo <see cref="Permisos"/>.
/// Los de sistema (<see cref="CodigoSistema"/> no nulo) no se eliminan ni se renombran; el Administrador
/// además es inmutable y tiene todos los permisos, así el comercio nunca queda sin quién lo gestione.
/// </summary>
public sealed class Rol : TenantEntity
{
    public const int LongitudMinimaNombre = 3;
    public const int LongitudMaximaNombre = 50;
    public const int LongitudMaximaDescripcion = 250;
    public const int LongitudMaximaCodigo = 30;

    private readonly List<RolPermiso> _permisos = [];

    public string Nombre { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }

    /// <summary>Clave estable de los roles predeterminados (<see cref="RolesSistema"/>); null en los personalizados.</summary>
    public string? CodigoSistema { get; private set; }

    public bool EstaActivo { get; private set; }

    /// <summary>Concurrencia optimista: dos administradores editando el mismo rol a la vez.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<RolPermiso> Permisos => _permisos.AsReadOnly();

    public bool EsSistema => CodigoSistema is not null;
    public bool EsAdministrador => CodigoSistema == RolesSistema.Administrador;

    private Rol(Guid id) : base(id) { }

    // Requerido por EF Core
    private Rol() { }

    public static Rol CrearPersonalizado(string nombre, string? descripcion, IEnumerable<string> permisos)
    {
        var rol = new Rol(Guid.NewGuid()) { EstaActivo = true };
        rol.EstablecerDatos(nombre, descripcion);
        rol.ReemplazarPermisos(permisos);
        return rol;
    }

    /// <summary>Los tres roles con los que nace cada comercio.</summary>
    public static IReadOnlyList<Rol> CrearRolesDeSistema() =>
    [
        CrearDeSistema(RolesSistema.Administrador, "Administrador", "Acceso total al comercio, incluida la gestión de usuarios y roles.", []),
        CrearDeSistema(RolesSistema.Supervisor, "Supervisor", "Mostrador, cierre y arqueo de caja, anulaciones, compras y reportes.", RolesSistema.PermisosSupervisor),
        CrearDeSistema(RolesSistema.Cajero, "Cajero", "Vende, abre caja y registra cobros.", RolesSistema.PermisosCajero)
    ];

    private static Rol CrearDeSistema(string codigo, string nombre, string descripcion, IEnumerable<string> permisos)
    {
        var rol = new Rol(Guid.NewGuid()) { EstaActivo = true, CodigoSistema = codigo };
        rol.EstablecerDatos(nombre, descripcion);
        rol.ReemplazarPermisos(permisos);
        return rol;
    }

    public bool Tiene(string permiso) => EsAdministrador || _permisos.Any(p => p.Codigo == permiso);

    /// <summary>Permisos efectivos: el Administrador tiene todo el catálogo.</summary>
    public IReadOnlyList<string> PermisosEfectivos() =>
        EsAdministrador ? Seguridad.Permisos.Todos : _permisos.Select(p => p.Codigo).ToList();

    public void Modificar(string nombre, string? descripcion)
    {
        if (EsSistema && NormalizarNombre(nombre) != Nombre)
            throw new DomainException("Los roles predeterminados no se pueden renombrar.");
        EstablecerDatos(nombre, descripcion);
    }

    /// <summary>Reemplaza el conjunto completo de permisos. Devuelve true si cambió algo.</summary>
    public bool AsignarPermisos(IEnumerable<string> permisos)
    {
        if (EsAdministrador)
            throw new DomainException("El rol Administrador tiene todos los permisos y no se puede modificar.");
        return ReemplazarPermisos(permisos);
    }

    public void Desactivar()
    {
        if (EsSistema)
            throw new DomainException("Los roles predeterminados no se pueden eliminar.");
        EstaActivo = false;
        MarcarModificado();
    }

    private bool ReemplazarPermisos(IEnumerable<string> permisos)
    {
        var nuevos = permisos.Select(p => p?.Trim() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var invalido = nuevos.FirstOrDefault(p => !Seguridad.Permisos.Existe(p));
        if (invalido is not null)
            throw new DomainException($"El permiso '{invalido}' no existe.");

        var actuales = _permisos.Select(p => p.Codigo).ToHashSet(StringComparer.Ordinal);
        if (actuales.SetEquals(nuevos))
            return false;

        _permisos.RemoveAll(p => !nuevos.Contains(p.Codigo));
        foreach (var codigo in nuevos.Where(c => !actuales.Contains(c)).Order(StringComparer.Ordinal))
            _permisos.Add(new RolPermiso(codigo));
        MarcarModificado();
        return true;
    }

    private void EstablecerDatos(string nombre, string? descripcion)
    {
        var normalizado = NormalizarNombre(nombre);
        if (normalizado.Length is < LongitudMinimaNombre or > LongitudMaximaNombre)
            throw new DomainException($"El nombre del rol debe tener entre {LongitudMinimaNombre} y {LongitudMaximaNombre} caracteres.");
        var desc = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        if (desc is { Length: > LongitudMaximaDescripcion })
            throw new DomainException($"La descripción del rol no puede superar {LongitudMaximaDescripcion} caracteres.");

        Nombre = normalizado;
        Descripcion = desc;
        MarcarModificado();
    }

    public static string NormalizarNombre(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? string.Empty : string.Join(' ', nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Permiso otorgado a un rol (tabla RolesPermisos, clave RolId + Codigo).</summary>
public sealed class RolPermiso
{
    public string Codigo { get; private set; } = string.Empty;

    internal RolPermiso(string codigo) => Codigo = codigo;

    // Requerido por EF Core
    private RolPermiso() { }
}
