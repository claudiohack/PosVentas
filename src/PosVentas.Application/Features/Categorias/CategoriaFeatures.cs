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
