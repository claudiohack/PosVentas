using System.Security.Cryptography;
using System.Text;

namespace PosVentas.WebAPI.Security;

/// <summary>
/// Validación de la firma de los webhooks de Mercado Pago (encabezado <c>x-signature: ts=...,v1=...</c>):
/// HMAC-SHA256 con la clave secreta de la aplicación sobre <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>.
/// </summary>
public static class FirmaWebhookMercadoPago
{
    public static bool EsValida(string? encabezadoFirma, string? requestId, string? dataId, string secreto)
    {
        if (string.IsNullOrWhiteSpace(encabezadoFirma) || string.IsNullOrWhiteSpace(secreto))
            return false;

        string? ts = null, v1 = null;
        foreach (var parte in encabezadoFirma.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = parte.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            if (kv[0] == "ts") ts = kv[1];
            else if (kv[0] == "v1") v1 = kv[1];
        }
        if (ts is null || v1 is null)
            return false;

        var esperada = Calcular(ts, requestId, dataId, secreto);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperada), Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }

    /// <summary>Firma esperada (hex en minúsculas). Pública para que los tests firmen avisos simulados.</summary>
    public static string Calcular(string ts, string? requestId, string? dataId, string secreto)
    {
        // Mercado Pago normaliza a minúsculas los ids alfanuméricos; las partes ausentes se omiten.
        var manifiesto = new StringBuilder();
        if (!string.IsNullOrEmpty(dataId)) manifiesto.Append("id:").Append(dataId.All(char.IsLetterOrDigit) ? dataId.ToLowerInvariant() : dataId).Append(';');
        if (!string.IsNullOrEmpty(requestId)) manifiesto.Append("request-id:").Append(requestId).Append(';');
        manifiesto.Append("ts:").Append(ts).Append(';');

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes(manifiesto.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
