using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Features.Suscripciones;
using PosVentas.Domain.Common;

namespace PosVentas.Infrastructure.MercadoPago;

/// <summary>
/// Cliente de la API de Suscripciones de Mercado Pago (https://api.mercadopago.com, /preapproval y /authorized_payments).
/// Errores de datos que rechaza Mercado Pago → 422 con su mensaje; caída o timeout → 503 reintentable.
/// </summary>
public sealed class MercadoPagoService(HttpClient http, IOptions<MercadoPagoOptions> opciones, ILogger<MercadoPagoService> logger)
    : IMercadoPagoService
{
    public const string UrlBase = "https://api.mercadopago.com/";
    private const string Moneda = "ARS";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool Habilitado => !string.IsNullOrWhiteSpace(opciones.Value.AccessToken);

    public async Task<PreaprobacionCreada> CrearPreaprobacionAsync(SolicitudPreaprobacion s, CancellationToken ct)
    {
        var cuerpo = new
        {
            reason = s.Motivo,
            external_reference = s.ReferenciaExterna,
            payer_email = s.EmailPagador,
            back_url = s.UrlRetorno,
            status = "pending",
            auto_recurring = new { frequency = 1, frequency_type = "months", transaction_amount = s.MontoMensual, currency_id = Moneda }
        };

        using var pedido = Pedido(HttpMethod.Post, "preapproval", cuerpo);
        pedido.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString("N"));
        var respuesta = await EnviarAsync<PreaprobacionApi>(pedido, ct)
            ?? throw new MercadoPagoNoDisponibleException("Mercado Pago no devolvió la suscripción creada.");
        if (string.IsNullOrWhiteSpace(respuesta.Id) || string.IsNullOrWhiteSpace(respuesta.InitPoint))
            throw new MercadoPagoNoDisponibleException("Mercado Pago devolvió una suscripción incompleta.");

        return new PreaprobacionCreada(respuesta.Id, respuesta.InitPoint);
    }

    public async Task<PreaprobacionMercadoPago?> ObtenerPreaprobacionAsync(string id, CancellationToken ct)
    {
        using var pedido = Pedido(HttpMethod.Get, $"preapproval/{Uri.EscapeDataString(id)}");
        var p = await EnviarAsync<PreaprobacionApi>(pedido, ct, nullSiNoExiste: true);
        return p is null ? null
            : new PreaprobacionMercadoPago(p.Id ?? id, p.Status ?? string.Empty, p.ExternalReference, p.AutoRecurring?.TransactionAmount ?? 0m);
    }

    public async Task<CobroMercadoPago?> ObtenerCobroAsync(string id, CancellationToken ct)
    {
        using var pedido = Pedido(HttpMethod.Get, $"authorized_payments/{Uri.EscapeDataString(id)}");
        var c = await EnviarAsync<CobroApi>(pedido, ct, nullSiNoExiste: true);
        if (c is null)
            return null;

        // Acreditado = cobro procesado y con el pago aprobado (los "recycling" se reintentan y llegan después).
        var aprobado = c.Status == "processed" && c.Payment?.Status == "approved";
        return new CobroMercadoPago(c.Id.ToString(CultureInfo.InvariantCulture), c.PreapprovalId ?? string.Empty,
            c.TransactionAmount, aprobado, $"{c.Status}/{c.Payment?.Status}");
    }

    public async Task CancelarPreaprobacionAsync(string id, CancellationToken ct)
    {
        using var pedido = Pedido(HttpMethod.Put, $"preapproval/{Uri.EscapeDataString(id)}", new { status = "cancelled" });
        await EnviarAsync<PreaprobacionApi>(pedido, ct);
    }

    private HttpRequestMessage Pedido(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var token = opciones.Value.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException("El pago con Mercado Pago no está configurado.");

        var pedido = new HttpRequestMessage(metodo, ruta);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cuerpo is not null)
            pedido.Content = JsonContent.Create(cuerpo, options: Json);
        return pedido;
    }

    private async Task<T?> EnviarAsync<T>(HttpRequestMessage pedido, CancellationToken ct, bool nullSiNoExiste = false) where T : class
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(pedido, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Mercado Pago no respondió ({Metodo} {Ruta}).", pedido.Method, pedido.RequestUri);
            throw new MercadoPagoNoDisponibleException("No se pudo comunicar con Mercado Pago. Probá de nuevo en unos minutos.", ex);
        }

        using (respuesta)
        {
            if (respuesta.StatusCode == HttpStatusCode.NotFound && nullSiNoExiste)
                return null;

            if (respuesta.IsSuccessStatusCode)
                return await respuesta.Content.ReadFromJsonAsync<T>(Json, ct);

            var detalle = await LeerMensajeAsync(respuesta, ct);
            logger.LogWarning("Mercado Pago respondió {Status} a {Metodo} {Ruta}: {Detalle}",
                (int)respuesta.StatusCode, pedido.Method, pedido.RequestUri, detalle);

            // 4xx: datos que Mercado Pago rechaza (email del pagador, monto…). 401/403: token inválido (problema nuestro).
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)respuesta.StatusCode >= 500)
                throw new MercadoPagoNoDisponibleException("Mercado Pago no está disponible en este momento. Probá de nuevo en unos minutos.");
            throw new DomainException($"Mercado Pago rechazó la operación: {detalle}");
        }
    }

    private static async Task<string> LeerMensajeAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var error = await respuesta.Content.ReadFromJsonAsync<ErrorApi>(Json, ct);
            return error?.Message ?? respuesta.ReasonPhrase ?? "error desconocido";
        }
        catch (JsonException)
        {
            return respuesta.ReasonPhrase ?? "error desconocido";
        }
    }

    // ---------- Formato de la API ----------

    private sealed record PreaprobacionApi(string? Id, string? Status, string? InitPoint, string? ExternalReference, RecurrenciaApi? AutoRecurring);

    private sealed record RecurrenciaApi(decimal TransactionAmount);

    private sealed record CobroApi(long Id, string? PreapprovalId, decimal TransactionAmount, string? Status, PagoApi? Payment);

    private sealed record PagoApi(long Id, string? Status);

    private sealed record ErrorApi(string? Message);
}
