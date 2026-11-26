using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Application.Features.Suscripciones;
using PosVentas.Domain.Enums;
using PosVentas.IntegrationTests.Infrastructure;
using PosVentas.WebAPI.Security;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class MercadoPagoTests(PosVentasApiFactory factory)
{
    private const string TipoSuscripcion = "subscription_preapproval";
    private const string TipoCobro = "subscription_authorized_payment";

    private MercadoPagoServiceFalso MercadoPago => factory.Services.GetRequiredService<MercadoPagoServiceFalso>();

    [Fact]
    public async Task Suscribirse_autorizar_y_cobrar_acredita_un_mes_y_los_avisos_repetidos_no_duplican()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var comercio = await PlanAsync(a, "COMERCIO");
        var antes = await MiSuscripcionAsync(a);
        Assert.True(antes.MercadoPagoHabilitado);

        // 1. El comercio elige el plan: Mercado Pago crea la suscripción pendiente y devuelve el enlace
        var email = $"{Guid.NewGuid():N}@testuser.com";
        var url = await IniciarPagoAsync(a, comercio.Id, email);
        Assert.StartsWith("https://www.mercadopago.com.ar/", url);
        var pre = MercadoPago.UltimaDe(email);
        Assert.Equal(30000m, pre.Monto);
        Assert.Contains("Plan Comercio", pre.Motivo);

        // Hasta que Mercado Pago confirme, no cambia nada
        Assert.Equal(("FULL", (EstadoDebitoAutomatico?)null), ((await MiSuscripcionAsync(a)).Plan.Codigo, (await MiSuscripcionAsync(a)).DebitoAutomatico));
        Assert.Equal(ResultadoNotificacion.Ignorada, await WebhookOkAsync(TipoSuscripcion, pre.Id)); // sigue pendiente

        // 2. Lo autoriza en Mercado Pago: pasa al plan elegido con débito automático activo
        MercadoPago.Autorizar(pre.Id);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoSuscripcion, pre.Id));
        Assert.Equal(ResultadoNotificacion.Duplicada, await WebhookOkAsync(TipoSuscripcion, pre.Id));
        var autorizada = await MiSuscripcionAsync(a);
        Assert.Equal(("COMERCIO", EstadoDebitoAutomatico.Activo, EstadoSuscripcion.Prueba),
            (autorizada.Plan.Codigo, autorizada.DebitoAutomatico, autorizada.Resumen.Estado));

        // 3. Primer cobro: se acredita un mes sobre lo que quedaba de prueba y deja de estar en prueba
        var cobro = MercadoPago.Cobrar(pre.Id);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoCobro, cobro));
        Assert.Equal(ResultadoNotificacion.Duplicada, await WebhookOkAsync(TipoCobro, cobro));

        var pagada = await MiSuscripcionAsync(a);
        var pago = Assert.Single(pagada.Pagos);
        Assert.Equal((30000m, 1, OrigenPagoSuscripcion.MercadoPago, cobro), (pago.Monto, pago.Meses, pago.Origen, pago.Referencia));
        Assert.Equal(EstadoSuscripcion.Activa, pagada.Resumen.Estado);
        Assert.Equal(antes.Resumen.VenceUtc!.Value.AddMonths(1), pagada.Resumen.VenceUtc!.Value, TimeSpan.FromSeconds(1));

        // Un cobro rechazado no acredita nada
        Assert.Equal(ResultadoNotificacion.Ignorada, await WebhookOkAsync(TipoCobro, MercadoPago.Cobrar(pre.Id, aprobado: false)));
        Assert.Single((await MiSuscripcionAsync(a)).Pagos);

        // Aislamiento: el otro comercio no se enteró de nada
        var deB = await MiSuscripcionAsync(b);
        Assert.Equal(("FULL", (EstadoDebitoAutomatico?)null, 0), (deB.Plan.Codigo, deB.DebitoAutomatico, deB.Pagos.Count));
    }

    [Fact]
    public async Task Un_aviso_sin_firma_o_con_firma_falsa_se_rechaza_y_uno_ajeno_se_ignora()
    {
        var a = await factory.RegistrarComercioAsync();
        var email = $"{Guid.NewGuid():N}@testuser.com";
        await IniciarPagoAsync(a, (await PlanAsync(a, "INICIAL")).Id, email);
        var pre = MercadoPago.UltimaDe(email);
        MercadoPago.Autorizar(pre.Id);

        using var http = factory.CreateClient();
        var sinFirma = await http.PostAsJsonAsync($"/api/webhooks/mercadopago?data.id={pre.Id}&type={TipoSuscripcion}",
            new { type = TipoSuscripcion, data = new { id = pre.Id } });
        Assert.Equal(HttpStatusCode.Unauthorized, sinFirma.StatusCode);

        var falsa = await WebhookAsync(TipoSuscripcion, pre.Id, secreto: "otra-clave");
        Assert.Equal(HttpStatusCode.Unauthorized, falsa.StatusCode);
        Assert.Null((await MiSuscripcionAsync(a)).DebitoAutomatico);

        // Suscripciones que no existen en Mercado Pago o tipos que no nos conciernen: 200, ignorados
        Assert.Equal(ResultadoNotificacion.Ignorada, await WebhookOkAsync(TipoSuscripcion, "no-existe"));
        Assert.Equal(ResultadoNotificacion.Ignorada, await WebhookOkAsync("payment", "456"));
        // Un cobro que todavía no se puede consultar NO se ignora: 503 para que Mercado Pago reintente
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await WebhookAsync(TipoCobro, "123")).StatusCode);
    }

    [Fact]
    public async Task El_cobro_avisado_antes_de_existir_en_Mercado_Pago_se_acredita_cuando_Mercado_Pago_reintenta()
    {
        var a = await factory.RegistrarComercioAsync();
        var email = $"{Guid.NewGuid():N}@testuser.com";
        await IniciarPagoAsync(a, (await PlanAsync(a, "COMERCIO")).Id, email);
        var pre = MercadoPago.UltimaDe(email);
        MercadoPago.Autorizar(pre.Id);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoSuscripcion, pre.Id));

        // Como en el sandbox real: el aviso del cobro llega y GET /authorized_payments/{id} todavía da 404
        var cobro = MercadoPago.CobrarConDemora(pre.Id);
        using (var primerIntento = await WebhookAsync(TipoCobro, cobro))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, primerIntento.StatusCode);
        Assert.Empty((await MiSuscripcionAsync(a)).Pagos);

        // Minutos después Mercado Pago reintenta el aviso y el cobro ya existe: se acredita una sola vez
        MercadoPago.HacerDisponible(cobro);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoCobro, cobro));
        Assert.Equal(ResultadoNotificacion.Duplicada, await WebhookOkAsync(TipoCobro, cobro));
        var pago = Assert.Single((await MiSuscripcionAsync(a)).Pagos);
        Assert.Equal((cobro, OrigenPagoSuscripcion.MercadoPago), (pago.Referencia, pago.Origen));
    }

    [Fact]
    public async Task Con_la_suscripcion_vencida_se_puede_pagar_y_el_cobro_la_reactiva()
    {
        var a = await factory.RegistrarComercioAsync();
        var plan = await PlanAsync(a, "COMERCIO");
        using var cajero = await factory.LoginConRolAsync(a, "cajero-mp", "Cajero"); // antes de vencer: después no se crean usuarios
        try
        {
            factory.Reloj.Adelantar(TimeSpan.FromDays(40));
            Assert.Equal(EstadoSuscripcion.SoloLectura, (await MiSuscripcionAsync(a)).Resumen.Estado);
            Assert.Equal(HttpStatusCode.PaymentRequired, (await a.CrearProductoAsync("MP-1", await a.CategoriaGeneralAsync())).StatusCode);

            // Un cajero no contrata planes; el Administrador sí, aunque esté en solo lectura
            Assert.Equal(HttpStatusCode.Forbidden, (await cajero.PostAsJsonAsync("/api/suscripcion/mercadopago",
                new { planId = plan.Id, emailPagador = "x@testuser.com" }, ApiTestHelpers.Json)).StatusCode);

            var email = $"{Guid.NewGuid():N}@testuser.com";
            await IniciarPagoAsync(a, plan.Id, email);
            var pre = MercadoPago.UltimaDe(email);
            MercadoPago.Autorizar(pre.Id);

            // El cobro llega antes que el aviso de la autorización: igual se vincula y se acredita
            Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoCobro, MercadoPago.Cobrar(pre.Id)));

            var reactivada = await MiSuscripcionAsync(a);
            Assert.Equal((EstadoSuscripcion.Activa, "COMERCIO", EstadoDebitoAutomatico.Activo),
                (reactivada.Resumen.Estado, reactivada.Plan.Codigo, reactivada.DebitoAutomatico));
            Assert.InRange(reactivada.Resumen.VenceUtc!.Value, factory.Reloj.AhoraUtc.AddDays(27), factory.Reloj.AhoraUtc.AddDays(32));
            Assert.Equal(HttpStatusCode.Created, (await a.CrearProductoAsync("MP-1", await a.CategoriaGeneralAsync())).StatusCode);
        }
        finally
        {
            factory.Reloj.Restablecer();
        }
    }

    [Fact]
    public async Task Cambiar_de_plan_cancela_el_debito_anterior_y_el_comercio_puede_cancelar_el_suyo()
    {
        var a = await factory.RegistrarComercioAsync();
        var email = $"{Guid.NewGuid():N}@testuser.com";

        await IniciarPagoAsync(a, (await PlanAsync(a, "COMERCIO")).Id, email);
        var primero = MercadoPago.UltimaDe(email);
        MercadoPago.Autorizar(primero.Id);
        await WebhookOkAsync(TipoSuscripcion, primero.Id);

        await IniciarPagoAsync(a, (await PlanAsync(a, "INICIAL")).Id, email);
        var segundo = MercadoPago.UltimaDe(email);
        Assert.NotEqual(primero.Id, segundo.Id);
        MercadoPago.Autorizar(segundo.Id);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoSuscripcion, segundo.Id));

        Assert.Equal("INICIAL", (await MiSuscripcionAsync(a)).Plan.Codigo);
        Assert.Contains(primero.Id, MercadoPago.Canceladas);
        Assert.Equal(EstadosPreaprobacionCancelada, MercadoPago.Obtener(primero.Id).Estado);
        // El aviso de la cancelación del débito viejo no toca el vigente
        Assert.Equal(ResultadoNotificacion.Ignorada, await WebhookOkAsync(TipoSuscripcion, primero.Id));
        Assert.Equal(EstadoDebitoAutomatico.Activo, (await MiSuscripcionAsync(a)).DebitoAutomatico);

        // Pausado desde Mercado Pago
        MercadoPago.Pausar(segundo.Id);
        Assert.Equal(ResultadoNotificacion.Procesada, await WebhookOkAsync(TipoSuscripcion, segundo.Id));
        Assert.Equal(EstadoDebitoAutomatico.Pausado, (await MiSuscripcionAsync(a)).DebitoAutomatico);

        // Cancelado desde PosVentas
        Assert.Equal(HttpStatusCode.NoContent, (await a.Http.DeleteAsync("/api/suscripcion/mercadopago")).StatusCode);
        Assert.Contains(segundo.Id, MercadoPago.Canceladas);
        Assert.Equal(EstadoDebitoAutomatico.Cancelado, (await MiSuscripcionAsync(a)).DebitoAutomatico);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Http.DeleteAsync("/api/suscripcion/mercadopago")).StatusCode);
    }

    private const string EstadosPreaprobacionCancelada = Application.Common.Interfaces.EstadosPreaprobacion.Cancelada;

    // ---------- Helpers ----------

    private static async Task<MiSuscripcionDto> MiSuscripcionAsync(ComercioDePrueba comercio) =>
        (await comercio.Http.GetFromJsonAsync<MiSuscripcionDto>("/api/suscripcion", ApiTestHelpers.Json))!;

    private static async Task<PlanDto> PlanAsync(ComercioDePrueba comercio, string codigo) =>
        Assert.Single((await MiSuscripcionAsync(comercio)).PlanesDisponibles, p => p.Codigo == codigo);

    private static async Task<string> IniciarPagoAsync(ComercioDePrueba comercio, Guid planId, string email)
    {
        var respuesta = await comercio.Http.PostAsJsonAsync("/api/suscripcion/mercadopago", new { planId, emailPagador = email }, ApiTestHelpers.Json);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<InicioPagoDto>(ApiTestHelpers.Json))!.UrlPago;
    }

    /// <summary>Envía un aviso como lo haría Mercado Pago: query + cuerpo JSON + x-signature y x-request-id.</summary>
    private async Task<HttpResponseMessage> WebhookAsync(string tipo, string id, string? secreto = null)
    {
        using var http = factory.CreateClient();
        var requestId = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var firma = FirmaWebhookMercadoPago.Calcular(ts, requestId, id, secreto ?? factory.SecretoWebhookMercadoPago);

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/mercadopago?data.id={Uri.EscapeDataString(id)}&type={tipo}")
        {
            Content = JsonContent.Create(new { action = "updated", type = tipo, data = new { id } })
        };
        pedido.Headers.Add("x-request-id", requestId);
        pedido.Headers.Add("x-signature", $"ts={ts},v1={firma}");
        return await http.SendAsync(pedido);
    }

    private async Task<ResultadoNotificacion> WebhookOkAsync(string tipo, string id)
    {
        using var respuesta = await WebhookAsync(tipo, id);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        var json = JsonNode.Parse(await respuesta.Content.ReadAsStringAsync())!;
        return Enum.Parse<ResultadoNotificacion>(json["resultado"]!.GetValue<string>());
    }
}
