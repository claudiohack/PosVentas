using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Invitación de un solo uso para dar de alta un comercio nuevo. La emite un administrador de la plataforma
/// (HS Tech). Se guarda solo el hash del token: el token en claro se entrega una única vez al crearla.
/// No pertenece a ningún comercio (no es una entidad de tenant).
/// </summary>
public sealed class InvitacionComercio : Entity<Guid>
{
    public const int LongitudHash = 64; // SHA-256 en hexadecimal
    public const int LongitudMaximaEmail = 150;
    public const int LongitudMaximaNota = 250;
    public const int DiasVigenciaMaximos = 30;

    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>A quién se le envió (informativo: no restringe quién la usa).</summary>
    public string? EmailDestinatario { get; private set; }

    public string? Nota { get; private set; }
    public Guid CreadaPorUsuarioId { get; private set; }
    public DateTime VenceUtc { get; private set; }
    public DateTime? UsadaUtc { get; private set; }
    public Guid? ComercioCreadoId { get; private set; }
    public DateTime? RevocadaUtc { get; private set; }

    /// <summary>Concurrencia optimista: dos altas simultáneas con el mismo token → solo una se confirma.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool EstaVigente => UsadaUtc is null && RevocadaUtc is null && VenceUtc > DateTime.UtcNow;

    private InvitacionComercio(Guid id) : base(id) { }

    // Requerido por EF Core
    private InvitacionComercio() { }

    public static InvitacionComercio Crear(string tokenHash, string? email, string? nota, int diasVigencia, Guid creadaPorUsuarioId)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != LongitudHash)
            throw new DomainException("El hash del token de invitación no es válido.");
        if (diasVigencia is < 1 or > DiasVigenciaMaximos)
            throw new DomainException($"La invitación debe vencer en 1 a {DiasVigenciaMaximos} días.");
        if (creadaPorUsuarioId == Guid.Empty)
            throw new DomainException("El usuario que emite la invitación es obligatorio.");

        return new InvitacionComercio(Guid.NewGuid())
        {
            TokenHash = tokenHash,
            EmailDestinatario = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim(),
            CreadaPorUsuarioId = creadaPorUsuarioId,
            VenceUtc = DateTime.UtcNow.AddDays(diasVigencia)
        };
    }

    /// <summary>Marca la invitación como usada por el comercio recién creado.</summary>
    public void Consumir(Guid comercioId)
    {
        if (!EstaVigente)
            throw new DomainException("La invitación no es válida o ya venció.");
        UsadaUtc = DateTime.UtcNow;
        ComercioCreadoId = comercioId;
        MarcarModificado();
    }

    public void Revocar()
    {
        if (UsadaUtc is not null)
            throw new DomainException("La invitación ya fue usada: no se puede revocar.");
        RevocadaUtc ??= DateTime.UtcNow;
        MarcarModificado();
    }
}
