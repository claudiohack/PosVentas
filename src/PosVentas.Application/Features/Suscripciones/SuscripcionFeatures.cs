using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Common.Suscripciones;
using PosVentas.Application.Features.Auth;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Suscripciones;

namespace PosVentas.Application.Features.Suscripciones;

// ---------- DTOs ----------

public sealed record ModuloPlanDto(string Codigo, string Nombre, string Descripcion);

public sealed record PlanDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    decimal PrecioMensual,
    int? MaxUsuarios,
    int? MaxProductos,
    IReadOnlyList<string> Modulos,
    int Orden,
    bool EstaActivo,
    bool EsPredeterminado);

public sealed record PagoSuscripcionDto(
    Guid Id, DateTime FechaUtc, decimal Monto, int Meses, DateTime PeriodoHastaUtc, OrigenPagoSuscripcion Origen,
    string Plan, string? Referencia, string? Nota);

/// <summary>Lo que ve el comercio de su suscripción: estado, plan, uso frente a los límites y planes a los que puede pasar.</summary>
public sealed record MiSuscripcionDto(
    SuscripcionResumenDto Resumen,
    PlanDto Plan,
    int UsuariosActivos,
    int ProductosActivos,
    IReadOnlyList<PlanDto> PlanesDisponibles,
    IReadOnlyList<PagoSuscripcionDto> Pagos,
    IReadOnlyList<ModuloPlanDto> Modulos,
    bool MercadoPagoHabilitado,
    EstadoDebitoAutomatico? DebitoAutomatico);

/// <summary>Fila de la administración de comercios de HS Tech.</summary>
public sealed record ComercioSuscripcionDto(
    Guid ComercioId,
    string Nombre,
    string Slug,
    string? Cuit,
    DateTime FechaAltaUtc,
    bool EstaActivo,
    Guid PlanId,
    string Plan,
    EstadoSuscripcion Estado,
    bool EnPrueba,
    DateTime VenceUtc,
    int DiasRestantes,
    DateTime? UltimoPagoUtc,
    EstadoDebitoAutomatico? DebitoAutomatico);

/// <summary>Nombres visibles de los módulos (el catálogo en sí vive en el dominio).</summary>
public static class CatalogoModulos
{
    public static IReadOnlyList<ModuloPlanDto> Todos { get; } =
    [
        new(ModulosPlan.Facturacion, "Facturación electrónica", "Facturas A, B y C con CAE de ARCA."),
        new(ModulosPlan.Compras, "Compras y proveedores", "Registro de compras, proveedores y su impacto en el stock."),
        new(ModulosPlan.Reportes, "Reportes", "Dashboard, ventas por período, margen y medios de pago."),
        new(ModulosPlan.Planillas, "Planillas Excel", "Importación y exportación del catálogo de productos."),
        new(ModulosPlan.Auditoria, "Auditoría", "Consulta de quién hizo qué y cuándo.")
    ];

    public static IReadOnlyList<string> Ordenar(IEnumerable<string> modulos)
    {
        var set = modulos.ToHashSet(StringComparer.Ordinal);
        return ModulosPlan.Todos.Where(set.Contains).ToList();
    }
}

internal static class SuscripcionMapper
{
    public static PlanDto ToDto(Plan p) => new(p.Id, p.Codigo, p.Nombre, p.Descripcion, p.PrecioMensual, p.MaxUsuarios, p.MaxProductos,
        CatalogoModulos.Ordenar(p.Modulos.Select(m => m.Codigo)), p.Orden, p.EstaActivo, p.EsPredeterminado);

    public static ComercioSuscripcionDto ToDto(Comercio c, Suscripcion s, DateTime? ultimoPago, DateTime ahoraUtc) =>
        new(c.Id, c.Nombre, c.Slug, c.Cuit, c.FechaCreacionUtc, c.EstaActivo, s.PlanId, s.Plan!.Nombre, s.EstadoEn(ahoraUtc), s.EnPrueba,
            s.VenceUtc, s.DiasRestantesEn(ahoraUtc), ultimoPago, s.DebitoAutomatico);

    public static async Task<IReadOnlyList<PagoSuscripcionDto>> PagosAsync(IApplicationDbContext db, Guid comercioId, CancellationToken ct) =>
        await db.PagosSuscripcion.AsNoTracking()
            .Where(p => p.ComercioId == comercioId)
            .OrderByDescending(p => p.FechaCreacionUtc)
            .Take(100)
            .Join(db.Planes, p => p.PlanId, pl => pl.Id, (p, pl) =>
                new PagoSuscripcionDto(p.Id, p.FechaCreacionUtc, p.Monto, p.Meses, p.PeriodoHastaUtc, p.Origen, pl.Nombre, p.Referencia, p.Nota))
            .ToListAsync(ct);
}

// ---------- El comercio: su suscripción ----------

public sealed record ObtenerMiSuscripcionQuery : IRequest<MiSuscripcionDto>;

public sealed class ObtenerMiSuscripcionQueryHandler(
    IApplicationDbContext db, ICurrentTenantService tenant, SuscripcionActual actual, IMercadoPagoService mercadoPago)
    : IRequestHandler<ObtenerMiSuscripcionQuery, MiSuscripcionDto>
{
    public async Task<MiSuscripcionDto> Handle(ObtenerMiSuscripcionQuery request, CancellationToken ct)
    {
        var comercioId = tenant.TenantId ?? throw new TenantAccessException("No hay un comercio en el request.");
        var suscripcion = await db.Suscripciones.AsNoTracking().Include(s => s.Plan).ThenInclude(p => p!.Modulos)
                              .SingleOrDefaultAsync(s => s.ComercioId == comercioId, ct)
                          ?? throw new NotFoundException("la suscripción del comercio", comercioId);

        var planes = await db.Planes.AsNoTracking().Include(p => p.Modulos)
            .Where(p => p.EstaActivo).OrderBy(p => p.Orden).ThenBy(p => p.PrecioMensual).ToListAsync(ct);

        return new MiSuscripcionDto(
            SuscripcionResumenDto.Desde(await actual.ObtenerAsync(ct)),
            SuscripcionMapper.ToDto(suscripcion.Plan!),
            await db.Usuarios.CountAsync(u => u.EstaActivo, ct),
            await db.Productos.CountAsync(p => p.EstaActivo, ct),
            planes.Select(SuscripcionMapper.ToDto).ToList(),
            await SuscripcionMapper.PagosAsync(db, comercioId, ct),
            CatalogoModulos.Todos,
            mercadoPago.Habilitado,
            suscripcion.DebitoAutomatico);
    }
}

// ---------- Plataforma: planes ----------

public sealed record ObtenerPlanesQuery : IRequest<IReadOnlyList<PlanDto>>, IRequiereAdministradorPlataforma;

public sealed class ObtenerPlanesQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerPlanesQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> Handle(ObtenerPlanesQuery request, CancellationToken ct) =>
        (await db.Planes.AsNoTracking().Include(p => p.Modulos).OrderBy(p => p.Orden).ThenBy(p => p.Nombre).ToListAsync(ct))
        .Select(SuscripcionMapper.ToDto).ToList();
}

public sealed record ObtenerModulosPlanQuery : IRequest<IReadOnlyList<ModuloPlanDto>>, IRequiereAdministradorPlataforma;

public sealed class ObtenerModulosPlanQueryHandler : IRequestHandler<ObtenerModulosPlanQuery, IReadOnlyList<ModuloPlanDto>>
{
    public Task<IReadOnlyList<ModuloPlanDto>> Handle(ObtenerModulosPlanQuery request, CancellationToken ct) =>
        Task.FromResult(CatalogoModulos.Todos);
}

/// <summary>Datos editables de un plan (comunes al alta y a la edición).</summary>
public interface IDatosPlan
{
    string Nombre { get; }
    string? Descripcion { get; }
    decimal PrecioMensual { get; }
    int? MaxUsuarios { get; }
    int? MaxProductos { get; }
    IReadOnlyList<string> Modulos { get; }
    int Orden { get; }
}

public sealed class DatosPlanValidator : AbstractValidator<IDatosPlan>
{
    public DatosPlanValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(Plan.LongitudMaximaNombre);
        RuleFor(x => x.Descripcion).MaximumLength(Plan.LongitudMaximaDescripcion);
        RuleFor(x => x.PrecioMensual).InclusiveBetween(0, Plan.PrecioMaximo).PrecisionScale(18, 2, true);
        RuleFor(x => x.MaxUsuarios).InclusiveBetween(1, Plan.LimiteMaximo).When(x => x.MaxUsuarios.HasValue);
        RuleFor(x => x.MaxProductos).InclusiveBetween(1, Plan.LimiteMaximo).When(x => x.MaxProductos.HasValue);
        RuleFor(x => x.Modulos).NotNull();
        RuleForEach(x => x.Modulos).Must(ModulosPlan.Existe).WithMessage("El módulo '{PropertyValue}' no existe.");
        RuleFor(x => x.Orden).InclusiveBetween(0, 1000);
    }
}

public sealed record CrearPlanCommand(
    string Codigo, string Nombre, string? Descripcion, decimal PrecioMensual, int? MaxUsuarios, int? MaxProductos,
    IReadOnlyList<string> Modulos, int Orden) : IRequest<PlanDto>, IDatosPlan, IRequiereAdministradorPlataforma;

public sealed class CrearPlanCommandValidator : AbstractValidator<CrearPlanCommand>
{
    public CrearPlanCommandValidator()
    {
        Include(new DatosPlanValidator());
        RuleFor(x => x.Codigo).NotEmpty().Matches("^[A-Za-z0-9_]{3,30}$")
            .WithMessage("El código debe tener 3 a 30 caracteres: letras, números y guiones bajos.");
    }
}

public sealed class CrearPlanCommandHandler(IApplicationDbContext db) : IRequestHandler<CrearPlanCommand, PlanDto>
{
    public async Task<PlanDto> Handle(CrearPlanCommand r, CancellationToken ct)
    {
        var plan = Plan.Crear(r.Codigo, r.Nombre, r.Descripcion, r.PrecioMensual, r.MaxUsuarios, r.MaxProductos, r.Modulos, r.Orden);
        if (await db.Planes.AnyAsync(p => p.Codigo == plan.Codigo, ct))
            throw new ConflictException($"Ya existe un plan con el código '{plan.Codigo}'.");

        db.Planes.Add(plan);
        await db.SaveChangesAsync(ct); // índice único por código cubre la carrera
        return SuscripcionMapper.ToDto(plan);
    }
}

/// <param name="EsPredeterminado">Marcarlo pasa la prueba gratuita de los comercios nuevos a este plan (el anterior deja de serlo).</param>
public sealed record ActualizarPlanCommand(
    Guid Id, string Nombre, string? Descripcion, decimal PrecioMensual, int? MaxUsuarios, int? MaxProductos,
    IReadOnlyList<string> Modulos, int Orden, bool EstaActivo, bool EsPredeterminado)
    : IRequest<PlanDto>, IDatosPlan, IRequiereAdministradorPlataforma;

public sealed class ActualizarPlanCommandValidator : AbstractValidator<ActualizarPlanCommand>
{
    public ActualizarPlanCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        Include(new DatosPlanValidator());
    }
}

public sealed class ActualizarPlanCommandHandler(IApplicationDbContext db) : IRequestHandler<ActualizarPlanCommand, PlanDto>
{
    public async Task<PlanDto> Handle(ActualizarPlanCommand r, CancellationToken ct)
    {
        var plan = await db.Planes.Include(p => p.Modulos).SingleOrDefaultAsync(p => p.Id == r.Id, ct)
            ?? throw new NotFoundException("el plan", r.Id);

        // Los cambios de precio rigen desde el próximo cobro; los de límites y módulos, desde el próximo request.
        plan.Modificar(r.Nombre, r.Descripcion, r.PrecioMensual, r.MaxUsuarios, r.MaxProductos, r.Modulos, r.Orden);

        if (r.EstaActivo) plan.Activar();
        if (r.EsPredeterminado && !plan.EsPredeterminado)
        {
            if (!r.EstaActivo)
                throw new DomainException("Solo un plan activo puede ser el predeterminado.");
            var anterior = await db.Planes.SingleOrDefaultAsync(p => p.EsPredeterminado, ct);
            anterior?.QuitarPredeterminado();
            plan.MarcarPredeterminado();
        }
        else if (!r.EsPredeterminado && plan.EsPredeterminado)
        {
            throw new DomainException("Siempre tiene que haber un plan predeterminado: marcá otro plan como predeterminado en su lugar.");
        }
        if (!r.EstaActivo) plan.Desactivar(); // el predeterminado no se desactiva (regla de dominio)

        await db.SaveChangesAsync(ct); // RowVersion: dos administradores editando el mismo plan → 409
        return SuscripcionMapper.ToDto(plan);
    }
}

// ---------- Plataforma: comercios y sus suscripciones ----------

public sealed record ObtenerComerciosSuscripcionQuery : IRequest<IReadOnlyList<ComercioSuscripcionDto>>, IRequiereAdministradorPlataforma;

public sealed class ObtenerComerciosSuscripcionQueryHandler(IApplicationDbContext db, SuscripcionActual actual)
    : IRequestHandler<ObtenerComerciosSuscripcionQuery, IReadOnlyList<ComercioSuscripcionDto>>
{
    public async Task<IReadOnlyList<ComercioSuscripcionDto>> Handle(ObtenerComerciosSuscripcionQuery request, CancellationToken ct)
    {
        var filas = await db.Comercios.AsNoTracking()
            .Where(c => !c.EsPlataforma)
            .Join(db.Suscripciones.AsNoTracking().Include(s => s.Plan), c => c.Id, s => s.ComercioId, (c, s) => new
            {
                Comercio = c,
                Suscripcion = s,
                UltimoPago = db.PagosSuscripcion.Where(p => p.ComercioId == c.Id).Max(p => (DateTime?)p.FechaCreacionUtc)
            })
            .OrderBy(x => x.Suscripcion.VenceUtc)
            .Take(1000)
            .ToListAsync(ct);

        return filas.Select(f => SuscripcionMapper.ToDto(f.Comercio, f.Suscripcion, f.UltimoPago, actual.AhoraUtc)).ToList();
    }
}

public sealed record ObtenerPagosComercioQuery(Guid ComercioId) : IRequest<IReadOnlyList<PagoSuscripcionDto>>, IRequiereAdministradorPlataforma;

public sealed class ObtenerPagosComercioQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerPagosComercioQuery, IReadOnlyList<PagoSuscripcionDto>>
{
    public async Task<IReadOnlyList<PagoSuscripcionDto>> Handle(ObtenerPagosComercioQuery request, CancellationToken ct)
    {
        if (!await db.Suscripciones.AnyAsync(s => s.ComercioId == request.ComercioId, ct))
            throw new NotFoundException("la suscripción del comercio", request.ComercioId);
        return await SuscripcionMapper.PagosAsync(db, request.ComercioId, ct);
    }
}

public sealed record CambiarPlanComercioCommand(Guid ComercioId, Guid PlanId) : IRequest<ComercioSuscripcionDto>, IRequiereAdministradorPlataforma;

public sealed class CambiarPlanComercioCommandValidator : AbstractValidator<CambiarPlanComercioCommand>
{
    public CambiarPlanComercioCommandValidator()
    {
        RuleFor(x => x.ComercioId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
    }
}

public sealed class CambiarPlanComercioCommandHandler(IApplicationDbContext db, SuscripcionActual actual)
    : IRequestHandler<CambiarPlanComercioCommand, ComercioSuscripcionDto>
{
    public async Task<ComercioSuscripcionDto> Handle(CambiarPlanComercioCommand request, CancellationToken ct)
    {
        var (comercio, suscripcion) = await GestionSuscripciones.ObtenerAsync(db, request.ComercioId, ct);
        var plan = await db.Planes.SingleOrDefaultAsync(p => p.Id == request.PlanId, ct)
            ?? throw new NotFoundException("el plan", request.PlanId);

        // Bajar de plan no borra nada: si el comercio supera los límites nuevos, no puede dar de alta más hasta ajustarse.
        suscripcion.CambiarPlan(plan);
        await db.SaveChangesAsync(ct);
        return await GestionSuscripciones.ToDtoAsync(db, comercio, suscripcion, actual.AhoraUtc, ct);
    }
}

/// <param name="Monto">Importe cobrado; si se omite, el precio del plan por la cantidad de meses.</param>
public sealed record RegistrarPagoManualCommand(Guid ComercioId, int Meses, decimal? Monto, string? Nota)
    : IRequest<ComercioSuscripcionDto>, IRequiereAdministradorPlataforma;

public sealed class RegistrarPagoManualCommandValidator : AbstractValidator<RegistrarPagoManualCommand>
{
    public RegistrarPagoManualCommandValidator()
    {
        RuleFor(x => x.ComercioId).NotEmpty();
        RuleFor(x => x.Meses).InclusiveBetween(1, Suscripcion.MesesMaximosPorPago);
        RuleFor(x => x.Monto).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).When(x => x.Monto.HasValue);
        RuleFor(x => x.Nota).MaximumLength(PagoSuscripcion.LongitudMaximaNota);
    }
}

public sealed class RegistrarPagoManualCommandHandler(IApplicationDbContext db, ICurrentUserService usuario, SuscripcionActual actual)
    : IRequestHandler<RegistrarPagoManualCommand, ComercioSuscripcionDto>
{
    public async Task<ComercioSuscripcionDto> Handle(RegistrarPagoManualCommand r, CancellationToken ct)
    {
        var (comercio, suscripcion) = await GestionSuscripciones.ObtenerAsync(db, r.ComercioId, ct);
        var monto = r.Monto ?? suscripcion.Plan!.PrecioMensual * r.Meses;

        var pago = PagoSuscripcion.Acreditar(suscripcion, monto, r.Meses, OrigenPagoSuscripcion.Manual, null, r.Nota,
            usuario.UsuarioId, actual.AhoraUtc);
        db.PagosSuscripcion.Add(pago);
        await db.SaveChangesAsync(ct); // RowVersion de la suscripción: dos pagos a la vez no pisan el vencimiento
        return await GestionSuscripciones.ToDtoAsync(db, comercio, suscripcion, actual.AhoraUtc, ct);
    }
}

internal static class GestionSuscripciones
{
    public static async Task<(Comercio Comercio, Suscripcion Suscripcion)> ObtenerAsync(IApplicationDbContext db, Guid comercioId, CancellationToken ct)
    {
        var comercio = await db.Comercios.SingleOrDefaultAsync(c => c.Id == comercioId && !c.EsPlataforma, ct)
            ?? throw new NotFoundException("el comercio", comercioId);
        var suscripcion = await db.Suscripciones.Include(s => s.Plan).SingleOrDefaultAsync(s => s.ComercioId == comercioId, ct)
            ?? throw new NotFoundException("la suscripción del comercio", comercioId);
        return (comercio, suscripcion);
    }

    public static async Task<ComercioSuscripcionDto> ToDtoAsync(
        IApplicationDbContext db, Comercio comercio, Suscripcion suscripcion, DateTime ahoraUtc, CancellationToken ct)
    {
        var ultimoPago = await db.PagosSuscripcion.Where(p => p.ComercioId == comercio.Id).MaxAsync(p => (DateTime?)p.FechaCreacionUtc, ct);
        return SuscripcionMapper.ToDto(comercio, suscripcion, ultimoPago, ahoraUtc);
    }
}
