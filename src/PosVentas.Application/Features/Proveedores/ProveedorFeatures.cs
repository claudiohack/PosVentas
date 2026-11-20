using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Proveedores;

public sealed record ProveedorDto(
    Guid Id,
    string RazonSocial,
    string Cuit,
    string? Telefono,
    string? Email,
    string? Direccion,
    bool EstaActivo)
{
    internal static ProveedorDto Desde(Proveedor p) =>
        new(p.Id, p.RazonSocial, p.Cuit, p.Telefono, p.Email, p.Direccion, p.EstaActivo);
}

// ---------- Consultas ----------

public sealed record ObtenerProveedoresQuery(string? Buscar, bool IncluirInactivos = false) : IRequest<IReadOnlyList<ProveedorDto>>;

public sealed class ObtenerProveedoresQueryValidator : AbstractValidator<ObtenerProveedoresQuery>
{
    public ObtenerProveedoresQueryValidator() => RuleFor(x => x.Buscar).MaximumLength(Proveedor.LongitudMaximaRazonSocial);
}

public sealed class ObtenerProveedoresQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerProveedoresQuery, IReadOnlyList<ProveedorDto>>
{
    public async Task<IReadOnlyList<ProveedorDto>> Handle(ObtenerProveedoresQuery request, CancellationToken ct)
    {
        var query = db.Proveedores.AsNoTracking();
        if (!request.IncluirInactivos)
            query = query.Where(p => p.EstaActivo);

        if (!string.IsNullOrWhiteSpace(request.Buscar))
        {
            var termino = request.Buscar.Trim();
            var digitos = new string(termino.Where(char.IsAsciiDigit).ToArray());
            query = query.Where(p => p.RazonSocial.Contains(termino) || (digitos.Length > 0 && p.Cuit.Contains(digitos)));
        }

        return await query.OrderBy(p => p.RazonSocial).Take(500)
            .Select(p => new ProveedorDto(p.Id, p.RazonSocial, p.Cuit, p.Telefono, p.Email, p.Direccion, p.EstaActivo))
            .ToListAsync(ct);
    }
}

public sealed record ObtenerProveedorPorIdQuery(Guid Id) : IRequest<ProveedorDto>;

public sealed class ObtenerProveedorPorIdQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerProveedorPorIdQuery, ProveedorDto>
{
    public async Task<ProveedorDto> Handle(ObtenerProveedorPorIdQuery request, CancellationToken ct) =>
        ProveedorDto.Desde(await db.Proveedores.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new NotFoundException("el proveedor", request.Id));
}

// ---------- Alta y edición ----------

/// <summary>Datos editables de un proveedor (comunes al alta y a la edición).</summary>
public interface IDatosProveedor
{
    string RazonSocial { get; }
    string Cuit { get; }
    string? Telefono { get; }
    string? Email { get; }
    string? Direccion { get; }
}

public sealed record CrearProveedorCommand(string RazonSocial, string Cuit, string? Telefono, string? Email, string? Direccion)
    : IRequest<ProveedorDto>, IDatosProveedor;

public sealed record ActualizarProveedorCommand(Guid Id, string RazonSocial, string Cuit, string? Telefono, string? Email, string? Direccion)
    : IRequest<ProveedorDto>, IDatosProveedor;

public sealed class DatosProveedorValidator : AbstractValidator<IDatosProveedor>
{
    public DatosProveedorValidator()
    {
        RuleFor(x => x.RazonSocial).NotEmpty().MaximumLength(Proveedor.LongitudMaximaRazonSocial);
        RuleFor(x => x.Cuit).NotEmpty().Matches(@"^\d{2}-?\d{8}-?\d$")
            .WithMessage("El CUIT debe tener 11 dígitos (con o sin guiones).");
        RuleFor(x => x.Telefono).MaximumLength(Proveedor.LongitudMaximaTelefono);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(Proveedor.LongitudMaximaEmail)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Direccion).MaximumLength(Proveedor.LongitudMaximaDireccion);
    }
}

public sealed class CrearProveedorCommandValidator : AbstractValidator<CrearProveedorCommand>
{
    public CrearProveedorCommandValidator() => Include(new DatosProveedorValidator());
}

public sealed class ActualizarProveedorCommandValidator : AbstractValidator<ActualizarProveedorCommand>
{
    public ActualizarProveedorCommandValidator() => Include(new DatosProveedorValidator());
}

internal static class ReglasProveedor
{
    public static async Task ExigirCuitLibreAsync(IApplicationDbContext db, string cuitNormalizado, Guid? exceptoId, CancellationToken ct)
    {
        if (await db.Proveedores.AnyAsync(p => p.Cuit == cuitNormalizado && p.Id != exceptoId, ct))
            throw new ConflictException($"Ya existe un proveedor con el CUIT {cuitNormalizado} en este comercio.");
    }
}

public sealed class CrearProveedorCommandHandler(IApplicationDbContext db) : IRequestHandler<CrearProveedorCommand, ProveedorDto>
{
    public async Task<ProveedorDto> Handle(CrearProveedorCommand request, CancellationToken ct)
    {
        var proveedor = Proveedor.Crear(request.RazonSocial, request.Cuit, request.Telefono, request.Email, request.Direccion);
        await ReglasProveedor.ExigirCuitLibreAsync(db, proveedor.Cuit, null, ct);

        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync(ct); // el índice único (TenantId, Cuit) cubre la carrera entre dos altas
        return ProveedorDto.Desde(proveedor);
    }
}

public sealed class ActualizarProveedorCommandHandler(IApplicationDbContext db) : IRequestHandler<ActualizarProveedorCommand, ProveedorDto>
{
    public async Task<ProveedorDto> Handle(ActualizarProveedorCommand request, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.SingleOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new NotFoundException("el proveedor", request.Id);

        await ReglasProveedor.ExigirCuitLibreAsync(db, Proveedor.NormalizarCuit(request.Cuit), proveedor.Id, ct);
        proveedor.Actualizar(request.RazonSocial, request.Cuit, request.Telefono, request.Email, request.Direccion);
        await db.SaveChangesAsync(ct);
        return ProveedorDto.Desde(proveedor);
    }
}

// ---------- Estado ----------

public sealed record CambiarEstadoProveedorCommand(Guid Id, bool Activo) : IRequest<ProveedorDto>;

public sealed class CambiarEstadoProveedorCommandHandler(IApplicationDbContext db)
    : IRequestHandler<CambiarEstadoProveedorCommand, ProveedorDto>
{
    public async Task<ProveedorDto> Handle(CambiarEstadoProveedorCommand request, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.SingleOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new NotFoundException("el proveedor", request.Id);

        if (request.Activo) proveedor.Activar(); else proveedor.Desactivar();
        await db.SaveChangesAsync(ct);
        return ProveedorDto.Desde(proveedor);
    }
}
