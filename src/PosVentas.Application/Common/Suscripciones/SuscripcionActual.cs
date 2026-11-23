using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Suscripciones;

namespace PosVentas.Application.Common.Suscripciones;

/// <summary>Foto de la suscripción del comercio en el momento del request.</summary>
public sealed record SuscripcionVigente(
    bool SinRestricciones,
    EstadoSuscripcion Estado,
    bool EnPrueba,
    DateTime? VenceUtc,
    DateTime? FinGraciaUtc,
    int DiasRestantes,
    Guid? PlanId,
    string? Plan,
    IReadOnlySet<string> Modulos,
    int? MaxUsuarios,
    int? MaxProductos)
{
    /// <summary>El comercio operador (HS Tech) no tiene suscripción: todo habilitado, sin límites.</summary>
    public static SuscripcionVigente Plataforma { get; } = new(true, EstadoSuscripcion.Activa, false, null, null, 0, null, null,
        new HashSet<string>(ModulosPlan.Todos, StringComparer.Ordinal), null, null);

    /// <summary>Comercio sin suscripción (no debería ocurrir): falla cerrado, en solo lectura.</summary>
    public static SuscripcionVigente Inexistente { get; } = new(false, EstadoSuscripcion.SoloLectura, false, null, null, 0, null, null,
        new HashSet<string>(StringComparer.Ordinal), null, null);

    public bool PermiteEscribir => Estado != EstadoSuscripcion.SoloLectura;

    public bool Incluye(string modulo) => SinRestricciones || Modulos.Contains(modulo);

    /// <summary>Permisos del rol menos los de los módulos que el plan no incluye.</summary>
    public IReadOnlyList<string> FiltrarPermisos(IEnumerable<string> permisos)
    {
        if (SinRestricciones)
            return permisos.ToList();
        var excluidos = ModulosPlan.PermisosExcluidos(Modulos);
        return permisos.Where(p => !excluidos.Contains(p)).ToList();
    }
}

/// <summary>
/// Suscripción del comercio del request (una consulta, cacheada por request). La usa el middleware de sesión
/// para el modo solo lectura y los permisos del plan, y los casos de uso para los límites.
/// </summary>
public sealed class SuscripcionActual(IApplicationDbContext db, ICurrentTenantService tenant, IReloj reloj)
{
    private SuscripcionVigente? _vigente;
    private Guid _comercioCacheado;

    public DateTime AhoraUtc => reloj.AhoraUtc;

    public async Task<SuscripcionVigente> ObtenerAsync(CancellationToken ct)
    {
        var comercioId = tenant.TenantId ?? throw new TenantAccessException("No hay un comercio en el request.");
        if (_vigente is not null && _comercioCacheado == comercioId)
            return _vigente;

        _vigente = await ConsultarAsync(db, comercioId, AhoraUtc, ct);
        _comercioCacheado = comercioId;
        return _vigente;
    }

    public static async Task<SuscripcionVigente> ConsultarAsync(IApplicationDbContext db, Guid comercioId, DateTime ahoraUtc, CancellationToken ct)
    {
        if (await db.Comercios.AsNoTracking().AnyAsync(c => c.Id == comercioId && c.EsPlataforma, ct))
            return SuscripcionVigente.Plataforma;

        var suscripcion = await db.Suscripciones.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p!.Modulos)
            .SingleOrDefaultAsync(s => s.ComercioId == comercioId, ct);
        if (suscripcion is null)
            return SuscripcionVigente.Inexistente;

        var plan = suscripcion.Plan!;
        return new SuscripcionVigente(false, suscripcion.EstadoEn(ahoraUtc), suscripcion.EnPrueba, suscripcion.VenceUtc,
            suscripcion.FinGraciaUtc, suscripcion.DiasRestantesEn(ahoraUtc), plan.Id, plan.Nombre,
            plan.Modulos.Select(m => m.Codigo).ToHashSet(StringComparer.Ordinal), plan.MaxUsuarios, plan.MaxProductos);
    }
}

/// <summary>Límites y módulos del plan, verificados por los casos de uso (403 con un mensaje para el usuario).</summary>
public static class ReglasPlan
{
    public static async Task ExigirModuloAsync(SuscripcionActual suscripcion, string modulo, string queHace, CancellationToken ct)
    {
        var vigente = await suscripcion.ObtenerAsync(ct);
        if (!vigente.Incluye(modulo))
            throw new LimitePlanException($"Tu plan {vigente.Plan} no incluye {queHace}. Pasate a un plan superior para usarlo.");
    }

    /// <summary>Exige lugar para <paramref name="adicionales"/> usuarios activos más.</summary>
    public static async Task ExigirCupoUsuariosAsync(IApplicationDbContext db, SuscripcionActual suscripcion, CancellationToken ct, int adicionales = 1)
    {
        var vigente = await suscripcion.ObtenerAsync(ct);
        if (vigente.MaxUsuarios is not { } maximo)
            return;
        var activos = await db.Usuarios.CountAsync(u => u.EstaActivo, ct);
        if (activos + adicionales > maximo)
            throw new LimitePlanException(
                $"Tu plan {vigente.Plan} permite hasta {maximo} usuarios activos y ya tenés {activos}. Dá de baja uno o pasate a un plan superior.");
    }

    /// <summary>Exige que el total de productos activos no supere el plan (<paramref name="activosResultantes"/> = después del cambio).</summary>
    public static async Task ExigirCupoProductosAsync(SuscripcionActual suscripcion, int activosResultantes, CancellationToken ct)
    {
        var vigente = await suscripcion.ObtenerAsync(ct);
        if (vigente.MaxProductos is { } maximo && activosResultantes > maximo)
            throw new LimitePlanException(
                $"Tu plan {vigente.Plan} permite hasta {maximo:N0} productos activos (quedarían {activosResultantes:N0}). Dá de baja productos o pasate a un plan superior.");
    }

    public static async Task ExigirCupoProductosAsync(IApplicationDbContext db, SuscripcionActual suscripcion, CancellationToken ct, int adicionales = 1)
    {
        var vigente = await suscripcion.ObtenerAsync(ct);
        if (vigente.MaxProductos is null)
            return;
        var activos = await db.Productos.CountAsync(p => p.EstaActivo, ct);
        await ExigirCupoProductosAsync(suscripcion, activos + adicionales, ct);
    }
}
