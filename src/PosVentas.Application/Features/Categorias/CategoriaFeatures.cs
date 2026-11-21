using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Categorias;

public sealed record CategoriaDto(Guid Id, string Nombre, string? Descripcion, int OrdenDisplay, bool EstaActivo);

// ---------- Listado ----------

public sealed record ObtenerCategoriasQuery(bool IncluirInactivas = false) : IRequest<IReadOnlyList<CategoriaDto>>;

public sealed class ObtenerCategoriasQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerCategoriasQuery, IReadOnlyList<CategoriaDto>>
{
    public async Task<IReadOnlyList<CategoriaDto>> Handle(ObtenerCategoriasQuery request, CancellationToken cancellationToken)
    {
        var query = db.Categorias.AsNoTracking();
        if (!request.IncluirInactivas)
            query = query.Where(c => c.EstaActivo);

        return await query
            .OrderBy(c => c.OrdenDisplay).ThenBy(c => c.Nombre)
            .Select(c => new CategoriaDto(c.Id, c.Nombre, c.Descripcion, c.OrdenDisplay, c.EstaActivo))
            .ToListAsync(cancellationToken);
    }
}

// ---------- Alta ----------

public sealed record CrearCategoriaCommand(string Nombre, string? Descripcion, int OrdenDisplay) : IRequest<Guid>;

public sealed class CrearCategoriaCommandValidator : AbstractValidator<CrearCategoriaCommand>
{
    public CrearCategoriaCommandValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(Categoria.LongitudMaximaNombre);
        RuleFor(x => x.Descripcion).MaximumLength(Categoria.LongitudMaximaDescripcion);
        RuleFor(x => x.OrdenDisplay).GreaterThanOrEqualTo(0);
    }
}

public sealed class CrearCategoriaCommandHandler(IApplicationDbContext db) : IRequestHandler<CrearCategoriaCommand, Guid>
{
    public async Task<Guid> Handle(CrearCategoriaCommand request, CancellationToken cancellationToken)
    {
        var nombre = request.Nombre.Trim();
        if (await db.Categorias.AnyAsync(c => c.Nombre == nombre, cancellationToken))
            throw new ConflictException($"Ya existe la categoría '{nombre}'.");

        var categoria = Categoria.Crear(nombre, request.Descripcion, request.OrdenDisplay);
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync(cancellationToken);
        return categoria.Id;
    }
}

// ---------- Edición ----------

public sealed record ActualizarCategoriaCommand(Guid Id, string Nombre, string? Descripcion, int OrdenDisplay) : IRequest<CategoriaDto>;

public sealed class ActualizarCategoriaCommandValidator : AbstractValidator<ActualizarCategoriaCommand>
{
    public ActualizarCategoriaCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(Categoria.LongitudMaximaNombre);
        RuleFor(x => x.Descripcion).MaximumLength(Categoria.LongitudMaximaDescripcion);
        RuleFor(x => x.OrdenDisplay).InclusiveBetween(0, 100_000);
    }
}

public sealed class ActualizarCategoriaCommandHandler(IApplicationDbContext db) : IRequestHandler<ActualizarCategoriaCommand, CategoriaDto>
{
    public async Task<CategoriaDto> Handle(ActualizarCategoriaCommand request, CancellationToken ct)
    {
        var categoria = await db.Categorias.SingleOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Categoria), request.Id);

        var nombre = request.Nombre.Trim();
        if (await db.Categorias.AnyAsync(c => c.Nombre == nombre && c.Id != categoria.Id, ct))
            throw new ConflictException($"Ya existe la categoría '{nombre}'.");

        categoria.ModificarDatos(nombre, request.Descripcion);
        categoria.CambiarOrden(request.OrdenDisplay);
        await db.SaveChangesAsync(ct);
        return CategoriaMapper.ToDto(categoria);
    }
}

// ---------- Baja lógica ----------

/// <summary>Activa o da de baja una categoría. No se puede dar de baja mientras tenga productos activos.</summary>
public sealed record CambiarEstadoCategoriaCommand(Guid Id, bool Activa) : IRequest<CategoriaDto>;

public sealed class CambiarEstadoCategoriaCommandHandler(IApplicationDbContext db) : IRequestHandler<CambiarEstadoCategoriaCommand, CategoriaDto>
{
    public async Task<CategoriaDto> Handle(CambiarEstadoCategoriaCommand request, CancellationToken ct)
    {
        var categoria = await db.Categorias.SingleOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Categoria), request.Id);

        if (request.Activa)
        {
            categoria.Activar();
        }
        else
        {
            var activos = await db.Productos.CountAsync(p => p.CategoriaId == categoria.Id && p.EstaActivo, ct);
            if (activos > 0)
                throw new Domain.Common.DomainException(
                    $"La categoría '{categoria.Nombre}' tiene {activos} productos activos: pasalos a otra categoría o dalos de baja antes.");
            categoria.Desactivar();
        }

        await db.SaveChangesAsync(ct);
        return CategoriaMapper.ToDto(categoria);
    }
}

// ---------- Ordenamiento ----------

/// <summary>Reordena las categorías: la posición en la lista define el orden (0, 10, 20…, deja lugar para intercalar).</summary>
public sealed record ReordenarCategoriasCommand(IReadOnlyList<Guid> Ids) : IRequest<IReadOnlyList<CategoriaDto>>;

public sealed class ReordenarCategoriasCommandValidator : AbstractValidator<ReordenarCategoriasCommand>
{
    public ReordenarCategoriasCommandValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().Must(ids => ids.Count <= 500 && ids.Distinct().Count() == ids.Count)
            .WithMessage("Indicá las categorías en el orden deseado, sin repetir.");
    }
}

public sealed class ReordenarCategoriasCommandHandler(IApplicationDbContext db) : IRequestHandler<ReordenarCategoriasCommand, IReadOnlyList<CategoriaDto>>
{
    public async Task<IReadOnlyList<CategoriaDto>> Handle(ReordenarCategoriasCommand request, CancellationToken ct)
    {
        var categorias = await db.Categorias.Where(c => request.Ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var faltante = request.Ids.FirstOrDefault(id => !categorias.ContainsKey(id));
        if (faltante != Guid.Empty)
            throw new NotFoundException(nameof(Categoria), faltante); // incluye las de otro comercio

        for (var i = 0; i < request.Ids.Count; i++)
            categorias[request.Ids[i]].CambiarOrden(i * 10);
        await db.SaveChangesAsync(ct);

        return request.Ids.Select(id => CategoriaMapper.ToDto(categorias[id])).ToList();
    }
}

internal static class CategoriaMapper
{
    public static CategoriaDto ToDto(Categoria c) => new(c.Id, c.Nombre, c.Descripcion, c.OrdenDisplay, c.EstaActivo);
}
