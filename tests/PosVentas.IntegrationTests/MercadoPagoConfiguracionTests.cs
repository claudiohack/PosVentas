using PosVentas.Application.Features.Suscripciones;
using PosVentas.Infrastructure.MercadoPago;

namespace PosVentas.IntegrationTests;

/// <summary>Reglas puras de la integración con Mercado Pago (no levantan la API).</summary>
public sealed class MercadoPagoConfiguracionTests
{
    [Theory]
    [InlineData("https://pos.hstech.com.ar/suscripcion", true)]
    [InlineData("https://staging.hstech.com.ar/suscripcion", true)]
    [InlineData("https://localhost:7212/suscripcion", false)]
    [InlineData("https://127.0.0.1/suscripcion", false)]
    [InlineData("https://192.168.0.10/suscripcion", false)]
    [InlineData("https://10.1.2.3/suscripcion", false)]
    [InlineData("https://mi-pc.local/suscripcion", false)]
    [InlineData("https://servidor/suscripcion", false)]
    [InlineData("http://pos.hstech.com.ar/suscripcion", false)]
    [InlineData("no es una url", false)]
    [InlineData(null, false)]
    public void La_url_de_retorno_debe_ser_https_publica(string? url, bool publica) =>
        Assert.Equal(publica, MercadoPagoOptions.EsUrlPublica(url));

    [Fact]
    public void Sin_url_publica_no_esta_operativo_aunque_haya_token()
    {
        Assert.False(new MercadoPagoOptions { AccessToken = "APP_USR-x", UrlRetorno = "https://localhost:7212/suscripcion" }.Operativo);
        Assert.False(new MercadoPagoOptions { AccessToken = null, UrlRetorno = "https://pos.hstech.com.ar/suscripcion" }.Operativo);
        Assert.True(new MercadoPagoOptions { AccessToken = "APP_USR-x", UrlRetorno = "https://pos.hstech.com.ar/suscripcion" }.Operativo);
    }

    [Theory]
    [InlineData("Invalid value for back_url, must be a valid URL", "configuración")]
    [InlineData("Both payer and collector must be real or test users", "no es válida para este pago")]
    [InlineData("Invalid payer_email", "no aceptó el email")]
    [InlineData("Invalid transaction_amount", "importe")]
    [InlineData("Cannot operate between different countries", "Argentina")]
    [InlineData("something unexpected", "no pudo procesar")]
    [InlineData(null, "no pudo procesar")]
    public void Los_rechazos_de_Mercado_Pago_se_explican_en_castellano(string? original, string esperado)
    {
        var traducido = MercadoPagoService.TraducirRechazo(original);
        Assert.Contains(esperado, traducido);
        Assert.DoesNotContain("Invalid", traducido);
    }
}
