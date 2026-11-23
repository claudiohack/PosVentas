using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PosVentas.Application.Features.Suscripciones;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>
/// Avisos de servicios externos. Anónimos: se autentican con la firma del emisor y nunca se confía en su contenido
/// (el caso de uso vuelve a consultar el recurso a la API del emisor con nuestras credenciales).
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public sealed class WebhooksController(ISender mediator, IOptions<MercadoPagoOptions> opciones, ILogger<WebhooksController> logger)
    : ControllerBase
{
    private const int TamanioMaximo = 16 * 1024;

    /// <summary>
    /// Webhook de Mercado Pago (configurarlo en el panel de la aplicación con los eventos de Suscripciones).
    /// 200 = recibido (aunque se ignore); 401 = firma inválida; 5xx = Mercado Pago reintenta más tarde.
    /// </summary>
    [HttpPost("mercadopago")]
    [RequestSizeLimit(TamanioMaximo)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MercadoPago(CancellationToken ct)
    {
        var (tipoCuerpo, idCuerpo) = await LeerCuerpoAsync(ct);
        var tipo = Request.Query["type"].FirstOrDefault() ?? tipoCuerpo ?? Request.Query["topic"].FirstOrDefault();
        var idQuery = Request.Query["data.id"].FirstOrDefault();
        var id = idQuery ?? idCuerpo ?? Request.Query["id"].FirstOrDefault();

        var secreto = opciones.Value.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(secreto))
        {
            if (!FirmaWebhookMercadoPago.EsValida(Request.Headers["x-signature"], Request.Headers["x-request-id"], idQuery ?? id, secreto))
            {
                logger.LogWarning("Webhook de Mercado Pago con firma inválida (tipo {Tipo}, id {Id}).", tipo, id);
                return Unauthorized();
            }
        }
        else if (!opciones.Value.PermitirWebhookSinFirma)
        {
            // Sin clave configurada no se procesa: 503 para que Mercado Pago reintente cuando esté configurada.
            logger.LogError("Llegó un webhook de Mercado Pago pero falta MercadoPago:WebhookSecret.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(tipo) || string.IsNullOrWhiteSpace(id))
            return Ok(new { resultado = ResultadoNotificacion.Ignorada });

        var resultado = await mediator.Send(new ProcesarNotificacionMercadoPagoCommand(tipo, id), ct);
        return Ok(new { resultado });
    }

    private async Task<(string? Tipo, string? Id)> LeerCuerpoAsync(CancellationToken ct)
    {
        if (Request.ContentLength is 0 || !(Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
            return (null, null);
        try
        {
            using var json = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            var raiz = json.RootElement;
            var tipo = raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            string? id = null;
            if (raiz.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var i))
                id = i.ValueKind == JsonValueKind.String ? i.GetString() : i.GetRawText();
            return (tipo, id);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
