using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Fiscal;
using PosVentas.Domain.Seguridad;

namespace PosVentas.Application.Features.Auditoria;

public sealed record RegistroAuditoriaDto(
    Guid Id, DateTime FechaUtc, Guid? UsuarioId, string? Usuario, string Accion, string Entidad, Guid? EntidadId, string? Detalle);

public sealed record PaginaAuditoriaDto(IReadOnlyList<RegistroAuditoriaDto> Registros, int Total, int Pagina, int TamanioPagina);

/// <summary>Registros de auditoría del comercio, del más reciente al más viejo, con filtros opcionales.</summary>
[RequierePermiso(Permisos.AuditoriaConsultar)]
public sealed record ObtenerAuditoriaQuery(
    DateOnly? Desde = null, DateOnly? Hasta = null, string? Entidad = null, Guid? EntidadId = null, int Pagina = 1, int TamanioPagina = 50)
    : IRequest<PaginaAuditoriaDto>;

public sealed class ObtenerAuditoriaQueryValidator : AbstractValidator<ObtenerAuditoriaQuery>
{
    public ObtenerAuditoriaQueryValidator()
    {
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1);
        RuleFor(x => x.TamanioPagina).InclusiveBetween(1, 200);
        RuleFor(x => x.Hasta).GreaterThanOrEqualTo(x => x.Desde).When(x => x.Desde.HasValue && x.Hasta.HasValue)
            .WithMessage("La fecha hasta no puede ser anterior a la fecha desde.");
        RuleFor(x => x.Entidad).MaximumLength(Domain.Entities.RegistroAuditoria.LongitudMaximaEntidad);
    }
}

public sealed class ObtenerAuditoriaQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerAuditoriaQuery, PaginaAuditoriaDto>
{
    public async Task<PaginaAuditoriaDto> Handle(ObtenerAuditoriaQuery q, CancellationToken ct)
    {
        var consulta = db.RegistrosAuditoria.AsNoTracking();
        // Días calendario argentinos, extremos incluidos
        if (q.Desde is { } desde)
        {
            var inicioUtc = FechaFiscal.InicioDelDiaUtc(desde);
            consulta = consulta.Where(r => r.FechaCreacionUtc >= inicioUtc);
        }
        if (q.Hasta is { } hasta)
        {
            var finUtc = FechaFiscal.InicioDelDiaUtc(hasta.AddDays(1));
            consulta = consulta.Where(r => r.FechaCreacionUtc < finUtc);
        }
        if (!string.IsNullOrWhiteSpace(q.Entidad))
            consulta = consulta.Where(r => r.Entidad == q.Entidad);
        if (q.EntidadId is { } entidadId)
            consulta = consulta.Where(r => r.EntidadId == entidadId);

        var total = await consulta.CountAsync(ct);
        var registros = await consulta
            .OrderByDescending(r => r.FechaCreacionUtc).ThenByDescending(r => r.Id)
            .Skip((q.Pagina - 1) * q.TamanioPagina).Take(q.TamanioPagina)
            .Select(r => new RegistroAuditoriaDto(r.Id, r.FechaCreacionUtc, r.UsuarioId, r.NombreUsuario, r.Accion, r.Entidad, r.EntidadId, r.Detalle))
            .ToListAsync(ct);

        return new PaginaAuditoriaDto(registros, total, q.Pagina, q.TamanioPagina);
    }
}
