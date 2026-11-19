using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Clientes;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Ventas;
using PosVentas.Domain.Enums;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class CuentaCorrienteTests(PosVentasApiFactory factory)
{
    // Producto de prueba: costo 1000 + 30% = 1300 neto, IVA 21% → 1573 por unidad

    // ---------- a) Venta con cuenta corriente dentro del límite ----------

    [Fact]
    public async Task Venta_a_cuenta_corriente_dentro_del_limite_carga_el_saldo_del_cliente()
    {
        var a = await factory.RegistrarComercioAsync();
        var cliente = await a.CrearClienteAsync(limiteCredito: 5000m);
        var producto = await a.CrearProductoOkAsync("CTA-CTE", stockInicial: 10);
        var sesion = await a.AbrirCajaAsync(0m);

        // Venta 100% a cuenta corriente
        var respuesta1 = await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);
        Assert.Equal(HttpStatusCode.Created, respuesta1.StatusCode);
        var venta1 = (await respuesta1.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(cliente.Id, venta1.ClienteId);
        Assert.Equal(MedioPago.CuentaCorriente, Assert.Single(venta1.Pagos).Medio);

        var cuenta = await a.CuentaCorrienteAsync(cliente.Id);
        Assert.Equal(1573m, cuenta.Cliente.SaldoActual);
        Assert.Equal(3427m, cuenta.Cliente.CreditoDisponible);
        var cargo = Assert.Single(cuenta.Movimientos);
        Assert.Equal(TipoMovimientoCuentaCorriente.Cargo, cargo.Tipo);
        Assert.Equal(1573m, cargo.Monto);
        Assert.Equal(venta1.Id, cargo.VentaId);

        // Venta mixta: 573 en efectivo + 1000 a cuenta
        var respuesta2 = await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)],
            [("Efectivo", 573m), ("CuentaCorriente", 1000m)]);
        Assert.Equal(HttpStatusCode.Created, respuesta2.StatusCode);
        Assert.Equal(2573m, (await a.CuentaCorrienteAsync(cliente.Id)).Cliente.SaldoActual);

        // En la caja solo ingresó el efectivo; lo vendido a crédito se informa aparte
        var resumen = await a.ResumenCajaAsync(sesion.Id);
        Assert.Equal(573m, resumen.TotalesPorMedio.Single(t => t.Medio == MedioPago.Efectivo).Total);
        Assert.DoesNotContain(resumen.TotalesPorMedio, t => t.Medio == MedioPago.CuentaCorriente);
        Assert.Equal(2573m, resumen.TotalVentasCuentaCorriente);
        Assert.Equal(573m, resumen.EfectivoEsperado);
    }

    [Fact]
    public async Task Venta_que_llega_exactamente_al_limite_es_aceptada()
    {
        var a = await factory.RegistrarComercioAsync();
        var cliente = await a.CrearClienteAsync(limiteCredito: 1573m);
        var producto = await a.CrearProductoOkAsync("JUSTO", stockInicial: 2);
        var sesion = await a.AbrirCajaAsync(0m);

        var respuesta = await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(0m, (await a.CuentaCorrienteAsync(cliente.Id)).Cliente.CreditoDisponible);
    }

    // ---------- b) Rechazo por límite de crédito ----------

    [Fact]
    public async Task Venta_que_supera_el_limite_de_credito_devuelve_400_y_no_persiste_nada()
    {
        var a = await factory.RegistrarComercioAsync();
        var cliente = await a.CrearClienteAsync(limiteCredito: 1000m);
        var producto = await a.CrearProductoOkAsync("CARO", stockInicial: 5);
        var sesion = await a.AbrirCajaAsync(0m);

        var respuesta = await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>(ApiTestHelpers.Json);
        Assert.Equal("Crédito rechazado.", problema!.Title);
        Assert.Contains("Límite de crédito excedido", problema.Detail);

        var cuenta = await a.CuentaCorrienteAsync(cliente.Id);
        Assert.Equal(0m, cuenta.Cliente.SaldoActual);
        Assert.Empty(cuenta.Movimientos);
        var productoActual = await a.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{producto.Id}", ApiTestHelpers.Json);
        Assert.Equal(5m, productoActual!.StockActual);
        Assert.Equal(0, (await a.ResumenCajaAsync(sesion.Id)).CantidadVentas);
    }

    [Fact]
    public async Task Venta_a_cuenta_corriente_sin_cliente_o_con_cuenta_no_habilitada_devuelve_400()
    {
        var a = await factory.RegistrarComercioAsync();
        var sinCuenta = await a.CrearClienteAsync(limiteCredito: 0m, permiteCuentaCorriente: false);
        var producto = await a.CrearProductoOkAsync("SIN-CTA", stockInicial: 5);
        var sesion = await a.AbrirCajaAsync(0m);

        var sinCliente = await a.VenderAsync(sesion.Id, null, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);
        var noHabilitada = await a.VenderAsync(sesion.Id, sinCuenta.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);

        Assert.Equal(HttpStatusCode.BadRequest, sinCliente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noHabilitada.StatusCode);
    }

    // ---------- c) Recibo de cobranza ----------

    [Fact]
    public async Task Cobranza_reduce_el_saldo_del_cliente_e_ingresa_el_dinero_en_la_caja_activa()
    {
        var a = await factory.RegistrarComercioAsync();
        var cliente = await a.CrearClienteAsync(limiteCredito: 5000m);
        var producto = await a.CrearProductoOkAsync("COBRO", stockInicial: 5);
        var sesion = await a.AbrirCajaAsync(1000m);
        Assert.Equal(HttpStatusCode.Created,
            (await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)])).StatusCode);

        // Entrega de 1000 en efectivo
        var respuesta = await a.CobrarAsync(cliente.Id, sesion.Id, "Efectivo", 1000m);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var recibo = (await respuesta.Content.ReadFromJsonAsync<ReciboCobranzaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(1573m, recibo.SaldoAnterior);
        Assert.Equal(573m, recibo.SaldoResultante);

        // Entrega de 73 por transferencia
        Assert.Equal(HttpStatusCode.Created, (await a.CobrarAsync(cliente.Id, sesion.Id, "Transferencia", 73m)).StatusCode);

        var cuenta = await a.CuentaCorrienteAsync(cliente.Id);
        Assert.Equal(500m, cuenta.Cliente.SaldoActual);
        Assert.Equal(3, cuenta.Movimientos.Count);
        Assert.Equal(2, cuenta.Movimientos.Count(m => m.Tipo == TipoMovimientoCuentaCorriente.Abono));
        Assert.Equal(recibo.Id, cuenta.Movimientos.Single(m => m.Monto == 1000m).ReciboCobranzaId);

        // Impacto en caja: 1000 de fondo + 1000 cobrados en efectivo; la transferencia no suma al efectivo
        var resumen = await a.ResumenCajaAsync(sesion.Id);
        Assert.Equal(1000m, resumen.TotalesPorMedio.Single(t => t.Medio == MedioPago.Efectivo).Total);
        Assert.Equal(73m, resumen.TotalesPorMedio.Single(t => t.Medio == MedioPago.Transferencia).Total);
        Assert.Equal(2, resumen.CantidadCobranzas);
        Assert.Equal(1073m, resumen.TotalCobranzas);
        Assert.Equal(2000m, resumen.EfectivoEsperado);

        // El arqueo cuadra con el efectivo cobrado
        var cierre = await a.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesion.Id}/cierre", new { montoCierreReal = 2000m });
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var resultado = await cierre.Content.ReadFromJsonAsync<Application.Features.Cajas.ResumenSesionDto>(ApiTestHelpers.Json);
        Assert.Equal("Exacto", resultado!.ResultadoArqueo);
    }

    [Fact]
    public async Task Cobranza_invalida_no_modifica_saldo_ni_caja()
    {
        var a = await factory.RegistrarComercioAsync();
        var cliente = await a.CrearClienteAsync(limiteCredito: 5000m);
        var producto = await a.CrearProductoOkAsync("COBRO-MAL", stockInicial: 5);
        var sesion = await a.AbrirCajaAsync(0m);
        await a.VenderAsync(sesion.Id, cliente.Id, [(producto.Id, 1m)], [("CuentaCorriente", 1573m)]);

        var supera = await a.CobrarAsync(cliente.Id, sesion.Id, "Efectivo", 2000m);   // más que la deuda
        var tarjeta = await a.CobrarAsync(cliente.Id, sesion.Id, "Tarjeta", 100m);    // medio no admitido
        var negativo = await a.CobrarAsync(cliente.Id, sesion.Id, "Efectivo", -5m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, supera.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tarjeta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativo.StatusCode);
        Assert.Equal(1573m, (await a.CuentaCorrienteAsync(cliente.Id)).Cliente.SaldoActual);
        Assert.Equal(0, (await a.ResumenCajaAsync(sesion.Id)).CantidadCobranzas);
    }

    // ---------- d) Aislamiento multi-tenant ----------

    [Fact]
    public async Task Comercio_B_no_ve_saldos_ni_registra_cobranzas_ni_vende_a_credito_a_clientes_del_comercio_A()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var clienteA = await a.CrearClienteAsync(limiteCredito: 5000m);
        var productoA = await a.CrearProductoOkAsync("A-CTA", stockInicial: 5);
        var productoB = await b.CrearProductoOkAsync("B-CTA", stockInicial: 5);
        var sesionA = await a.AbrirCajaAsync(0m);
        var sesionB = await b.AbrirCajaAsync(0m);
        await a.VenderAsync(sesionA.Id, clienteA.Id, [(productoA.Id, 1m)], [("CuentaCorriente", 1573m)]);

        // Listado de B: no contiene clientes de A
        var clientesB = await b.Http.GetFromJsonAsync<List<ClienteDto>>("/api/clientes", ApiTestHelpers.Json);
        Assert.DoesNotContain(clientesB!, c => c.Id == clienteA.Id);

        // Saldo, cobranza, crédito y venta sobre el cliente de A → 404
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/clientes/{clienteA.Id}/cuenta-corriente")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.CobrarAsync(clienteA.Id, sesionB.Id, "Efectivo", 100m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.PutAsJsonAsync($"/api/clientes/{clienteA.Id}/credito",
            new { permiteCuentaCorriente = true, limiteCredito = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.VenderAsync(sesionB.Id, clienteA.Id, [(productoB.Id, 1m)], [("CuentaCorriente", 1573m)])).StatusCode);

        // Cobranza de B sobre su propio cliente usando la caja de A → 404
        var clienteB = await b.CrearClienteAsync(limiteCredito: 5000m);
        await b.VenderAsync(sesionB.Id, clienteB.Id, [(productoB.Id, 1m)], [("CuentaCorriente", 1573m)]);
        Assert.Equal(HttpStatusCode.NotFound, (await b.CobrarAsync(clienteB.Id, sesionA.Id, "Efectivo", 100m)).StatusCode);

        // A sigue intacto
        var cuentaA = await a.CuentaCorrienteAsync(clienteA.Id);
        Assert.Equal(1573m, cuentaA.Cliente.SaldoActual);
        Assert.Equal(5000m, cuentaA.Cliente.LimiteCredito);
        Assert.Single(cuentaA.Movimientos);
        Assert.Equal(0, (await a.ResumenCajaAsync(sesionA.Id)).CantidadCobranzas);
    }
}
