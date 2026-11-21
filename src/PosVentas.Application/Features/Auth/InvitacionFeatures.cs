using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Features.Auth;

/// <summary>Generación y hash de tokens de invitación. Solo el hash se persiste.</summary>
public static class TokensInvitacion
{
    /// <summary>256 bits aleatorios en Base64 URL-safe (43 caracteres): apto para un enlace.</summary>
    public static string Generar() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}

public sealed record InvitacionDto(
    Guid Id,
    string? EmailDestinatario,
    string? Nota,
    DateTime FechaCreacionUtc,
    DateTime VenceUtc,
    string Estado,
    DateTime? UsadaUtc,
    Guid? ComercioCreadoId);

/// <summary>Respuesta del alta: el token en claro se muestra UNA sola vez; después solo existe su hash.</summary>
public sealed record InvitacionCreadaDto(Guid Id, string Token, DateTime VenceUtc, string? EmailDestinatario);

internal static class InvitacionMapper
{
    public static string Estado(InvitacionComercio i) =>
        i.UsadaUtc is not null ? "Usada" : i.RevocadaUtc is not null ? "Revocada" : i.VenceUtc <= DateTime.UtcNow ? "Vencida" : "Vigente";

    public static InvitacionDto ToDto(InvitacionComercio i) =>
        new(i.Id, i.EmailDestinatario, i.Nota, i.FechaCreacionUtc, i.VenceUtc, Estado(i), i.UsadaUtc, i.ComercioCreadoId);
}

// ---------- Emisión ----------

public sealed record CrearInvitacionCommand(string? EmailDestinatario, string? Nota, int DiasVigencia = 7)
    : IRequest<InvitacionCreadaDto>, IRequiereAdministradorPlataforma;

public sealed class CrearInvitacionCommandValidator : AbstractValidator<CrearInvitacionCommand>
{
    public CrearInvitacionCommandValidator()
    {
        RuleFor(x => x.EmailDestinatario).EmailAddress().MaximumLength(InvitacionComercio.LongitudMaximaEmail)
            .When(x => !string.IsNullOrWhiteSpace(x.EmailDestinatario));
        RuleFor(x => x.Nota).MaximumLength(InvitacionComercio.LongitudMaximaNota);
        RuleFor(x => x.DiasVigencia).InclusiveBetween(1, InvitacionComercio.DiasVigenciaMaximos);
    }
}

public sealed class CrearInvitacionCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<CrearInvitacionCommand, InvitacionCreadaDto>
{
    public async Task<InvitacionCreadaDto> Handle(CrearInvitacionCommand request, CancellationToken ct)
    {
        var usuarioId = usuarioActual.UsuarioId ?? throw new AccesoDenegadoException("No hay un usuario autenticado.");
        var token = TokensInvitacion.Generar();

        var invitacion = InvitacionComercio.Crear(TokensInvitacion.Hash(token), request.EmailDestinatario, request.Nota,
            request.DiasVigencia, usuarioId);
        db.InvitacionesComercio.Add(invitacion);
        await db.SaveChangesAsync(ct);

        return new InvitacionCreadaDto(invitacion.Id, token, invitacion.VenceUtc, invitacion.EmailDestinatario);
    }
}

// ---------- Consulta y revocación ----------

public sealed record ObtenerInvitacionesQuery : IRequest<IReadOnlyList<InvitacionDto>>, IRequiereAdministradorPlataforma;

public sealed class ObtenerInvitacionesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerInvitacionesQuery, IReadOnlyList<InvitacionDto>>
{
    public async Task<IReadOnlyList<InvitacionDto>> Handle(ObtenerInvitacionesQuery request, CancellationToken ct)
    {
        var invitaciones = await db.InvitacionesComercio.AsNoTracking()
            .OrderByDescending(i => i.FechaCreacionUtc).Take(200).ToListAsync(ct);
        return invitaciones.Select(InvitacionMapper.ToDto).ToList();
    }
}

public sealed record RevocarInvitacionCommand(Guid Id) : IRequest<InvitacionDto>, IRequiereAdministradorPlataforma;

public sealed class RevocarInvitacionCommandHandler(IApplicationDbContext db) : IRequestHandler<RevocarInvitacionCommand, InvitacionDto>
{
    public async Task<InvitacionDto> Handle(RevocarInvitacionCommand request, CancellationToken ct)
    {
        var invitacion = await db.InvitacionesComercio.SingleOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new NotFoundException("la invitación", request.Id);
        invitacion.Revocar();
        await db.SaveChangesAsync(ct);
        return InvitacionMapper.ToDto(invitacion);
    }
}
