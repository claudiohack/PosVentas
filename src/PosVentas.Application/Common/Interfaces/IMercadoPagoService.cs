namespace PosVentas.Application.Common.Interfaces;

/// <summary>
/// Puerto hacia la API de Suscripciones de Mercado Pago (preapproval). HS Tech es el cobrador: el access token
/// es el de su cuenta. Los tests usan un simulador y nunca salen a internet.
/// </summary>
public interface IMercadoPagoService
{
    /// <summary>true si hay un access token configurado.</summary>
    bool Habilitado { get; }

    /// <summary>Crea la suscripción (pendiente de autorización) y devuelve el enlace donde el pagador la autoriza.</summary>
    Task<PreaprobacionCreada> CrearPreaprobacionAsync(SolicitudPreaprobacion solicitud, CancellationToken ct);

    /// <summary>Estado actual de una suscripción. null si no existe.</summary>
    Task<PreaprobacionMercadoPago?> ObtenerPreaprobacionAsync(string id, CancellationToken ct);

    /// <summary>Un cobro mensual de una suscripción ("authorized payment"). null si no existe.</summary>
    Task<CobroMercadoPago?> ObtenerCobroAsync(string id, CancellationToken ct);

    /// <summary>Cancela el débito automático (no hay más cobros).</summary>
    Task CancelarPreaprobacionAsync(string id, CancellationToken ct);
}

/// <param name="ReferenciaExterna">Nuestra referencia, viaja de vuelta en cada consulta (suscripción + plan).</param>
/// <param name="EmailPagador">Email de la cuenta de Mercado Pago que va a pagar.</param>
/// <param name="UrlRetorno">Adónde vuelve el pagador después de autorizar.</param>
public sealed record SolicitudPreaprobacion(
    string Motivo, string ReferenciaExterna, string EmailPagador, decimal MontoMensual, string UrlRetorno);

public sealed record PreaprobacionCreada(string Id, string UrlPago);

/// <param name="Estado">pending, authorized, paused o cancelled.</param>
public sealed record PreaprobacionMercadoPago(string Id, string Estado, string? ReferenciaExterna, decimal MontoMensual);

/// <param name="Aprobado">El cobro se acreditó (procesado y con el pago aprobado).</param>
public sealed record CobroMercadoPago(string Id, string PreaprobacionId, decimal Monto, bool Aprobado, string Estado);

/// <summary>Estados de una suscripción en Mercado Pago (los que nos importan).</summary>
public static class EstadosPreaprobacion
{
    public const string Pendiente = "pending";
    public const string Autorizada = "authorized";
    public const string Pausada = "paused";
    public const string Cancelada = "cancelled";
}
