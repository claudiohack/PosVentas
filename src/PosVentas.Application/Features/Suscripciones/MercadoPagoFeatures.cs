using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Suscripciones;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Suscripciones;

/// <summary>Configuración de Mercado Pago (sección "MercadoPago"). El access token va en User Secrets o variables de entorno.</summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    /// <summary>Access token de la cuenta que cobra (HS Tech). Vacío = pago con Mercado Pago deshabilitado.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Clave secreta de los webhooks (panel de la aplicación → Webhooks) para validar la firma x-signature.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Página del cliente a la que vuelve el comercio después de autorizar el pago (debe ser https pública).</summary>
    public string UrlRetorno { get; set; } = "https://pos.hstech.com.ar/suscripcion";

    /// <summary>Solo para desarrollo: acepta webhooks sin firma si no hay WebhookSecret (igual se consulta todo a la API).</summary>
    public bool PermitirWebhookSinFirma { get; set; }

    public int TimeoutSegundos { get; set; } = 20;

    /// <summary>
    /// Mercado Pago solo acepta una URL de retorno https pública. En un equipo local (localhost, IP privada) no es
    /// así, y tampoco le podrían llegar los webhooks: el pago con Mercado Pago queda deshabilitado en ese entorno.
    /// </summary>
    public bool UrlRetornoPublica => EsUrlPublica(UrlRetorno);

    /// <summary>Hay token y el entorno es alcanzable desde internet.</summary>
    public bool Operativo => !string.IsNullOrWhiteSpace(AccessToken) && UrlRetornoPublica;

    public static bool EsUrlPublica(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback)
            return false;
        var host = uri.Host;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.'))
            return false;
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var b = ip.GetAddressBytes();
            var privada = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
            return !privada && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal;
        }
        return true;
    }
}

public sealed record InicioPagoDto(string UrlPago);

/// <summary>Referencia que viaja con la suscripción de Mercado Pago: a qué suscripción y a qué plan corresponde.</summary>
internal static class ReferenciaMercadoPago
{
    public static string Crear(Guid suscripcionId, Guid planId) => $"{suscripcionId:N}:{planId:N}";

    public static bool TryLeer(string? referencia, out Guid suscripcionId, out Guid planId)
    {
        suscripcionId = planId = Guid.Empty;
        var partes = referencia?.Split(':');
        return partes is { Length: 2 }
               && Guid.TryParseExact(partes[0], "N", out suscripcionId)
               && Guid.TryParseExact(partes[1], "N", out planId);
    }
}

// ---------- El comercio inicia el débito automático ----------

/// <param name="EmailPagador">Email de la cuenta de Mercado Pago con la que va a pagar.</param>
public sealed record IniciarPagoMercadoPagoCommand(Guid PlanId, string EmailPagador) : IRequest<InicioPagoDto>;

public sealed class IniciarPagoMercadoPagoCommandValidator : AbstractValidator<IniciarPagoMercadoPagoCommand>
{
    public IniciarPagoMercadoPagoCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.EmailPagador).NotEmpty().EmailAddress().MaximumLength(150)
            .WithMessage("Ingresá el email de tu cuenta de Mercado Pago.");
    }
}

public sealed class IniciarPagoMercadoPagoCommandHandler(
    IApplicationDbContext db, ICurrentUserService usuario, ICurrentTenantService tenant, IMercadoPagoService mercadoPago,
    IOptions<MercadoPagoOptions> opciones)
    : IRequestHandler<IniciarPagoMercadoPagoCommand, InicioPagoDto>
{
    public async Task<InicioPagoDto> Handle(IniciarPagoMercadoPagoCommand request, CancellationToken ct)
    {
        if (!usuario.EsAdministrador)
            throw new AccesoDenegadoException("Solo el Administrador del comercio puede contratar o cambiar el plan.");
        if (!mercadoPago.Habilitado)
            throw new DomainException("El pago con Mercado Pago no está disponible por ahora. Para cambiar de plan o abonar, contactá a HS Tech.");

        var comercioId = tenant.TenantId ?? throw new TenantAccessException("No hay un comercio en el request.");
        var comercio = await db.Comercios.AsNoTracking().SingleAsync(c => c.Id == comercioId, ct);
        var suscripcion = await db.Suscripciones.AsNoTracking().SingleOrDefaultAsync(s => s.ComercioId == comercioId, ct)
            ?? throw new NotFoundException("la suscripción del comercio", comercioId);
        var plan = await db.Planes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PlanId && p.EstaActivo, ct)
            ?? throw new NotFoundException("el plan", request.PlanId);
        if (plan.PrecioMensual <= 0)
            throw new DomainException($"El plan {plan.Nombre} no tiene precio: no se cobra por Mercado Pago.");

        // Nada se guarda hasta que Mercado Pago confirme la autorización por webhook.
        var creada = await mercadoPago.CrearPreaprobacionAsync(new SolicitudPreaprobacion(
            $"PosVentas · Plan {plan.Nombre} · {comercio.Nombre}",
            ReferenciaMercadoPago.Crear(suscripcion.Id, plan.Id),
            request.EmailPagador.Trim().ToLowerInvariant(),
            plan.PrecioMensual,
            opciones.Value.UrlRetorno), ct);

        return new InicioPagoDto(creada.UrlPago);
    }
}

// ---------- El comercio cancela el débito automático ----------

public sealed record CancelarDebitoAutomaticoCommand : IRequest;

public sealed class CancelarDebitoAutomaticoCommandHandler(
    IApplicationDbContext db, ICurrentUserService usuario, ICurrentTenantService tenant, IMercadoPagoService mercadoPago)
    : IRequestHandler<CancelarDebitoAutomaticoCommand>
{
    public async Task Handle(CancelarDebitoAutomaticoCommand request, CancellationToken ct)
    {
        if (!usuario.EsAdministrador)
            throw new AccesoDenegadoException("Solo el Administrador del comercio puede cancelar el débito automático.");

        var comercioId = tenant.TenantId ?? throw new TenantAccessException("No hay un comercio en el request.");
        var suscripcion = await db.Suscripciones.SingleOrDefaultAsync(s => s.ComercioId == comercioId, ct)
            ?? throw new NotFoundException("la suscripción del comercio", comercioId);
        if (suscripcion.PreaprobacionMercadoPagoId is not { } id || suscripcion.DebitoAutomatico == EstadoDebitoAutomatico.Cancelado)
            throw new DomainException("No tenés un débito automático activo.");

        // Primero en Mercado Pago: si falla, no marcamos como cancelado algo que va a seguir cobrando.
        await mercadoPago.CancelarPreaprobacionAsync(id, ct);
        suscripcion.ActualizarDebitoAutomatico(EstadoDebitoAutomatico.Cancelado);
        await db.SaveChangesAsync(ct);
    }
}

// ---------- Avisos (webhooks) de Mercado Pago ----------

public enum ResultadoNotificacion
{
    Procesada = 1,

    /// <summary>No nos concierne (otro tipo de aviso, estado intermedio o recurso ajeno).</summary>
    Ignorada = 2,

    /// <summary>Ya se había procesado (Mercado Pago reintenta los avisos).</summary>
    Duplicada = 3
}

/// <summary>
/// Procesa un aviso de Mercado Pago. Nunca confía en el contenido del aviso: con el id consulta el recurso
/// a la API (con nuestro token) y actúa según lo que responde. Es idempotente.
/// </summary>
public sealed record ProcesarNotificacionMercadoPagoCommand(string Tipo, string RecursoId) : IRequest<ResultadoNotificacion>;

public sealed class ProcesarNotificacionMercadoPagoCommandHandler(
    IApplicationDbContext db, IMercadoPagoService mercadoPago, IReloj reloj, ILogger<ProcesarNotificacionMercadoPagoCommandHandler> logger)
    : IRequestHandler<ProcesarNotificacionMercadoPagoCommand, ResultadoNotificacion>
{
    public const string TipoSuscripcion = "subscription_preapproval";
    public const string TipoCobro = "subscription_authorized_payment";

    public async Task<ResultadoNotificacion> Handle(ProcesarNotificacionMercadoPagoCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RecursoId) || request.RecursoId.Length > Suscripcion.LongitudMaximaIdExterno)
            return ResultadoNotificacion.Ignorada;

        return request.Tipo switch
        {
            TipoSuscripcion or "preapproval" => await ProcesarSuscripcionAsync(request.RecursoId, ct),
            TipoCobro or "authorized_payment" => await ProcesarCobroAsync(request.RecursoId, ct),
            _ => ResultadoNotificacion.Ignorada
        };
    }

    private async Task<ResultadoNotificacion> ProcesarSuscripcionAsync(string id, CancellationToken ct)
    {
        var preaprobacion = await mercadoPago.ObtenerPreaprobacionAsync(id, ct);
        if (preaprobacion is null || !ReferenciaMercadoPago.TryLeer(preaprobacion.ReferenciaExterna, out var suscripcionId, out var planId))
        {
            logger.LogWarning("Aviso de Mercado Pago por la suscripción {Id}: no existe o no es nuestra.", id);
            return ResultadoNotificacion.Ignorada;
        }

        var suscripcion = await db.Suscripciones.Include(s => s.Plan).SingleOrDefaultAsync(s => s.Id == suscripcionId, ct);
        if (suscripcion is null)
        {
            logger.LogWarning("La suscripción {SuscripcionId} referida por Mercado Pago ({Id}) no existe.", suscripcionId, id);
            return ResultadoNotificacion.Ignorada;
        }

        switch (preaprobacion.Estado)
        {
            case EstadosPreaprobacion.Autorizada:
                if (suscripcion.PreaprobacionMercadoPagoId == id && suscripcion.DebitoAutomatico == EstadoDebitoAutomatico.Activo)
                    return ResultadoNotificacion.Duplicada;

                var plan = await db.Planes.SingleOrDefaultAsync(p => p.Id == planId, ct);
                if (plan is null)
                {
                    logger.LogError("Mercado Pago autorizó la suscripción {Id} de un plan inexistente ({PlanId}).", id, planId);
                    return ResultadoNotificacion.Ignorada;
                }

                var anterior = suscripcion.VincularDebitoAutomatico(id, plan);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Débito automático {Id} activo para la suscripción {SuscripcionId} (plan {Plan}).", id, suscripcion.Id, plan.Codigo);

                // Cambió de plan con un débito nuevo: el anterior se cancela para no cobrar dos veces.
                if (anterior is not null)
                    await CancelarAnteriorAsync(anterior, ct);
                return ResultadoNotificacion.Procesada;

            case EstadosPreaprobacion.Pausada or EstadosPreaprobacion.Cancelada when suscripcion.PreaprobacionMercadoPagoId == id:
                var nuevo = preaprobacion.Estado == EstadosPreaprobacion.Pausada ? EstadoDebitoAutomatico.Pausado : EstadoDebitoAutomatico.Cancelado;
                if (suscripcion.DebitoAutomatico == nuevo)
                    return ResultadoNotificacion.Duplicada;
                suscripcion.ActualizarDebitoAutomatico(nuevo);
                await db.SaveChangesAsync(ct);
                return ResultadoNotificacion.Procesada;

            default:
                return ResultadoNotificacion.Ignorada; // pendiente, o un débito viejo que ya no es el vigente
        }
    }

    private async Task<ResultadoNotificacion> ProcesarCobroAsync(string id, CancellationToken ct)
    {
        var cobro = await mercadoPago.ObtenerCobroAsync(id, ct);
        if (cobro is null || !cobro.Aprobado)
        {
            logger.LogInformation("Cobro {Id} de Mercado Pago sin acreditar (estado {Estado}).", id, cobro?.Estado ?? "inexistente");
            return ResultadoNotificacion.Ignorada;
        }

        if (await YaAcreditadoAsync(cobro.Id, ct))
            return ResultadoNotificacion.Duplicada;

        // El aviso del cobro puede llegar antes que el de la autorización: si no está vinculado, se procesa primero.
        var suscripcion = await db.Suscripciones.Include(s => s.Plan)
            .SingleOrDefaultAsync(s => s.PreaprobacionMercadoPagoId == cobro.PreaprobacionId, ct);
        if (suscripcion is null)
        {
            await ProcesarSuscripcionAsync(cobro.PreaprobacionId, ct);
            suscripcion = await db.Suscripciones.Include(s => s.Plan)
                .SingleOrDefaultAsync(s => s.PreaprobacionMercadoPagoId == cobro.PreaprobacionId, ct);
        }
        if (suscripcion is null)
        {
            logger.LogWarning("Cobro {Id} de una suscripción de Mercado Pago desconocida ({Preaprobacion}).", id, cobro.PreaprobacionId);
            return ResultadoNotificacion.Ignorada;
        }

        var pago = PagoSuscripcion.Acreditar(suscripcion, cobro.Monto, 1, OrigenPagoSuscripcion.MercadoPago, cobro.Id,
            "Débito automático de Mercado Pago", null, reloj.AhoraUtc);
        db.PagosSuscripcion.Add(pago);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // Dos avisos del mismo cobro a la vez: el índice único frenó al segundo. Otro conflicto se propaga.
            db.PagosSuscripcion.Remove(pago);
            if (await YaAcreditadoAsync(cobro.Id, ct))
                return ResultadoNotificacion.Duplicada;
            throw;
        }

        logger.LogInformation("Cobro {Id} acreditado: suscripción {SuscripcionId} vence el {Vence:yyyy-MM-dd}.", id, suscripcion.Id, pago.PeriodoHastaUtc);
        return ResultadoNotificacion.Procesada;
    }

    private Task<bool> YaAcreditadoAsync(string referencia, CancellationToken ct) =>
        db.PagosSuscripcion.AsNoTracking().AnyAsync(p => p.Origen == OrigenPagoSuscripcion.MercadoPago && p.Referencia == referencia, ct);

    private async Task CancelarAnteriorAsync(string id, CancellationToken ct)
    {
        try
        {
            await mercadoPago.CancelarPreaprobacionAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No se revierte el cambio de plan: queda en el log para cancelarlo a mano desde Mercado Pago.
            logger.LogError(ex, "No se pudo cancelar el débito automático anterior {Id}: cancelalo desde el panel de Mercado Pago.", id);
        }
    }
}
