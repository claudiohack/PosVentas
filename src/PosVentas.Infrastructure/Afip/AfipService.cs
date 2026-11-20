using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;
using PosVentas.Infrastructure.Persistence;

namespace PosVentas.Infrastructure.Afip;

/// <summary>
/// Implementación real de <see cref="IAfipService"/> contra WSAA + WSFEv1.
/// - Reutiliza el ticket de acceso (≈12 h) guardado cifrado en el comercio: WSAA rechaza pedir otro mientras siga vigente.
/// - Numera con FECompUltimoAutorizado + 1 y, si otro proceso ganó ese número (error 10016), reintenta una vez.
/// - Si se corta la comunicación después de enviar FECAESolicitar, consulta el número con FECompConsultar
///   para no perder un CAE que ARCA sí haya otorgado.
/// </summary>
public sealed class AfipService(
    AfipSoapCliente soap,
    CertificadoAfipProtector protector,
    IServiceScopeFactory scopes,
    ILogger<AfipService> logger) : IAfipService
{
    private const int ErrorNumeroNoCorrelativo = 10016;
    private static readonly TimeSpan MargenVencimientoTicket = TimeSpan.FromMinutes(10);

    public async Task<ResultadoAutorizacionAfip> AutorizarComprobanteAsync(SolicitudComprobanteAfip s, CancellationToken ct)
    {
        var tipo = TiposComprobanteAfip.Codigo(s.Tipo);

        for (var intento = 1; ; intento++)
        {
            var cred = await CredencialesAsync(s.ComercioId, renovar: false, ct);
            long numero;
            try
            {
                numero = await soap.UltimoAutorizadoAsync(cred, s.Produccion, s.PuntoVenta, tipo, ct) + 1;
            }
            catch (CredencialesAfipInvalidasException)
            {
                cred = await CredencialesAsync(s.ComercioId, renovar: true, ct);
                numero = await soap.UltimoAutorizadoAsync(cred, s.Produccion, s.PuntoVenta, tipo, ct) + 1;
            }

            RespuestaCae respuesta;
            try
            {
                respuesta = await soap.SolicitarCaeAsync(cred, s, tipo, numero, ct);
            }
            catch (AfipNoDisponibleException ex)
            {
                // ¿Llegó a autorizarse antes del corte? Si ARCA lo tiene, se recupera el CAE.
                var recuperado = await IntentarRecuperarAsync(cred, s, tipo, numero);
                if (recuperado is not null)
                {
                    logger.LogWarning(ex, "Se recuperó el CAE del comprobante {Tipo} {PuntoVenta}-{Numero} tras un corte de comunicación.",
                        s.Tipo, s.PuntoVenta, numero);
                    return recuperado;
                }
                throw;
            }

            if (respuesta.Resultado == "A" && respuesta.Cae is not null)
                return new ResultadoAutorizacionAfip(true, respuesta.Numero ?? numero, respuesta.Cae, respuesta.Vencimiento,
                    respuesta.Errores, respuesta.Observaciones);

            var numeroOcupado = respuesta.Errores.Concat(respuesta.Observaciones).Any(e => e.Codigo == ErrorNumeroNoCorrelativo);
            if (numeroOcupado && intento < 2)
                continue;

            logger.LogInformation("ARCA rechazó {Tipo} {PuntoVenta}-{Numero}: {Errores}",
                s.Tipo, s.PuntoVenta, numero, string.Join(" | ", respuesta.Errores.Concat(respuesta.Observaciones)));
            return new ResultadoAutorizacionAfip(false, respuesta.Numero, null, null, respuesta.Errores, respuesta.Observaciones);
        }
    }

    public async Task<EstadoServicioAfip> VerificarConexionAsync(Guid comercioId, TipoComprobante tipo, CancellationToken ct)
    {
        var comercio = await LeerComercioAsync(comercioId, ct);
        var produccion = comercio.AfipProduccion;

        var servidoresOk = await soap.DummyAsync(produccion, ct);
        var cred = await CredencialesAsync(comercioId, renovar: false, ct);
        var ultimo = await soap.UltimoAutorizadoAsync(cred, produccion, comercio.PuntoVentaAfip!.Value, TiposComprobanteAfip.Codigo(tipo), ct);

        return new EstadoServicioAfip(servidoresOk, Autenticado: true, ultimo, produccion,
            $"Conectado a ARCA ({(produccion ? "producción" : "homologación")}). Último {TiposComprobanteAfip.Descripcion(tipo)} " +
            $"del punto de venta {comercio.PuntoVentaAfip}: {ultimo}.");
    }

    private async Task<ResultadoAutorizacionAfip?> IntentarRecuperarAsync(CredencialesWsfe cred, SolicitudComprobanteAfip s, int tipo, long numero)
    {
        try
        {
            var consultado = await soap.ConsultarAsync(cred, s.Produccion, s.PuntoVenta, tipo, numero, CancellationToken.None);
            var esElNuestro = consultado is { Resultado: "A", Cae: not null }
                              && consultado.ImporteTotal == decimal.Round(s.ImporteTotal, 2)
                              && consultado.DocNro == s.DocNro;
            return esElNuestro
                ? new ResultadoAutorizacionAfip(true, numero, consultado!.Cae, consultado.Vencimiento, [], [])
                : null;
        }
        catch (Exception ex) when (ex is AfipNoDisponibleException or CredencialesAfipInvalidasException)
        {
            return null;
        }
    }

    /// <summary>Token y sign vigentes: reutiliza el ticket guardado o pide uno nuevo a WSAA y lo persiste cifrado.</summary>
    private async Task<CredencialesWsfe> CredencialesAsync(Guid comercioId, bool renovar, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var comercio = await db.Comercios.SingleAsync(c => c.Id == comercioId, ct);

        if (comercio.Cuit is null || comercio.CertificadoAfipProtegido is null)
            throw new Domain.Common.DomainException("El comercio no tiene configurada la facturación electrónica.");

        if (!renovar && comercio.TicketAccesoAfipProtegido is not null
                     && comercio.TicketAccesoAfipVenceUtc > DateTime.UtcNow + MargenVencimientoTicket)
        {
            var (token, sign) = protector.AbrirTicket(comercio.TicketAccesoAfipProtegido);
            return new CredencialesWsfe(token, sign, comercio.Cuit);
        }

        using var certificado = protector.Abrir(comercio.CertificadoAfipProtegido);
        var ticket = await soap.LoginAsync(certificado, comercio.AfipProduccion, ct);

        comercio.GuardarTicketAcceso(protector.ProtegerTicket(ticket.Token, ticket.Sign), ticket.VenceUtc);
        await db.SaveChangesAsync(ct);

        return new CredencialesWsfe(ticket.Token, ticket.Sign, comercio.Cuit);
    }

    private async Task<Comercio> LeerComercioAsync(Guid comercioId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Comercios.AsNoTracking().SingleAsync(c => c.Id == comercioId, ct);
    }
}
