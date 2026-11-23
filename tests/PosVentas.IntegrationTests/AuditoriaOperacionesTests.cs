using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PosVentas.Application.Features.Auditoria;
using PosVentas.Application.Features.Productos;
using PosVentas.Application.Features.Ventas;
using PosVentas.Infrastructure.Planillas;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

/// <summary>Auditoría de las operaciones del negocio: anulaciones, caja, productos, importación y configuración fiscal.</summary>
[Collection(ApiCollection.Nombre)]
public sealed class AuditoriaOperacionesTests(PosVentasApiFactory factory)
{
    private const string Motivo = "El cliente se arrepintió de la compra";

    [Fact]
    public async Task Registra_la_anulacion_de_una_venta_y_la_apertura_y_el_cierre_de_caja_con_su_arqueo()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("AUD-V1", precioCompra: 1000m, ganancia: 0m, iva: 0m, stockInicial: 10m);
        var sesion = await a.AbrirCajaAsync(2000m);

        var vendida = await a.VenderAsync(sesion.Id, null, [(producto.Id, 1m)], [("Efectivo", 1000m)]);
        Assert.Equal(HttpStatusCode.Created, vendida.StatusCode);
        var venta = (await vendida.Content.ReadFromJsonAsync<VentaDto>(ApiTestHelpers.Json))!;
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PostAsJsonAsync($"/api/ventas/{venta.Id}/anular",
            new { motivoAnulacion = Motivo }, ApiTestHelpers.Json)).StatusCode);

        // Quedan 2000 en efectivo (la venta se devolvió); se cuentan 1900: faltante de 100
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PostAsJsonAsync($"/api/cajas/sesiones/{sesion.Id}/cierre",
            new { montoCierreReal = 1900m, observaciones = "Faltó un billete" }, ApiTestHelpers.Json)).StatusCode);

        var anulacion = Assert.Single((await AuditoriaAsync(a, "Venta", venta.Id)).Registros);
        Assert.Equal("venta.anulada", anulacion.Accion);
        Assert.Contains(Motivo, anulacion.Detalle);
        Assert.Contains("1.000,00", anulacion.Detalle);
        Assert.Equal(a.Auth.UsuarioId, anulacion.UsuarioId);

        var caja = (await AuditoriaAsync(a, "CajaSesion", sesion.Id)).Registros;
        Assert.Equal(["caja.cerrada", "caja.abierta"], caja.Select(r => r.Accion));
        Assert.Contains("faltante de", caja[0].Detalle);
        Assert.Contains("100,00", caja[0].Detalle);
        Assert.Contains("Faltó un billete", caja[0].Detalle);
        Assert.Contains("2.000,00", caja[1].Detalle);
    }

    [Fact]
    public async Task Registra_alta_cambios_de_precio_ingresos_de_stock_y_bajas_de_productos_pero_no_ediciones_sin_cambio_de_precio()
    {
        var a = await factory.RegistrarComercioAsync();
        var producto = await a.CrearProductoOkAsync("AUD-P1", precioCompra: 1000m, ganancia: 30m, stockInicial: 5m);

        // Cambia solo el nombre: no se audita como cambio de precio
        Assert.Equal(HttpStatusCode.OK, (await Editar(a, producto, precioCompra: 1000m)).StatusCode);
        // Sube el costo: el precio de venta pasa de 1300 a 1560
        Assert.Equal(HttpStatusCode.OK, (await Editar(a, producto, precioCompra: 1200m)).StatusCode);
        // Baja dos veces: la segunda no cambia nada y no se registra
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/productos/{producto.Id}/estado", new { activo = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PatchAsJsonAsync($"/api/productos/{producto.Id}/estado", new { activo = false })).StatusCode);

        var registros = (await AuditoriaAsync(a, "Producto", producto.Id)).Registros;
        Assert.Equal(["producto.desactivado", "producto.precio-modificado", "producto.stock-ingresado", "producto.creado"],
            registros.Select(r => r.Accion));

        var precio = registros.Single(r => r.Accion == "producto.precio-modificado").Detalle!;
        Assert.Contains("1.300,00", precio);
        Assert.Contains("1.560,00", precio);
        Assert.Contains("0 → 5", registros.Single(r => r.Accion == "producto.stock-ingresado").Detalle);
    }

    [Fact]
    public async Task Registra_la_importacion_aplicada_pero_no_la_vista_previa_ni_una_planilla_con_errores()
    {
        var a = await factory.RegistrarComercioAsync();
        var valida = Planilla(["AUD-IMP-1", "Importado", null, "Bebidas", 100m, 30m, null, 21m, null, null, 3m, null]);
        var conErrores = Planilla(["AUD-IMP-2", "Costo inválido", null, "General", "abc", 30m, null, 21m, null, null, null, null]);

        Assert.True((await ImportarAsync(a, valida, simular: true)).StatusCode == HttpStatusCode.OK);
        Assert.True((await ImportarAsync(a, conErrores, simular: false)).StatusCode == HttpStatusCode.OK);
        Assert.Equal(0, (await AuditoriaAsync(a, "Producto")).Total);

        Assert.True((await ImportarAsync(a, valida, simular: false)).StatusCode == HttpStatusCode.OK);
        var importacion = Assert.Single((await AuditoriaAsync(a, "Producto")).Registros);
        Assert.Equal("productos.importados", importacion.Accion);
        Assert.Null(importacion.EntidadId);
        Assert.Contains("1 nuevos, 0 actualizados", importacion.Detalle);
        Assert.Contains("Bebidas", importacion.Detalle);
    }

    [Fact]
    public async Task Registra_los_datos_fiscales_y_la_carga_del_certificado_sin_exponer_su_contenido()
    {
        var a = await factory.RegistrarComercioFacturandoAsync("20111111112", "ResponsableInscripto");

        var registros = (await AuditoriaAsync(a, "Comercio")).Registros;
        Assert.Equal(["facturacion.certificado-cargado", "facturacion.datos-modificados"], registros.Select(r => r.Accion));
        Assert.Contains("20-11111111-2", registros[1].Detalle);
        Assert.Contains("Responsable Inscripto", registros[1].Detalle);
        Assert.Contains("homologación", registros[1].Detalle);
        Assert.Contains("posventas-test", registros[0].Detalle);
        Assert.DoesNotContain("BEGIN", registros[0].Detalle);

        // Otro comercio no ve estos registros
        var b = await factory.RegistrarComercioAsync();
        Assert.Equal(0, (await AuditoriaAsync(b, "Comercio")).Total);
    }

    // ---------- Helpers ----------

    private static async Task<PaginaAuditoriaDto> AuditoriaAsync(ComercioDePrueba comercio, string entidad, Guid? entidadId = null) =>
        (await comercio.Http.GetFromJsonAsync<PaginaAuditoriaDto>(
            $"/api/auditoria?entidad={entidad}" + (entidadId is { } id ? $"&entidadId={id}" : string.Empty), ApiTestHelpers.Json))!;

    private static Task<HttpResponseMessage> Editar(ComercioDePrueba comercio, ProductoDto producto, decimal precioCompra) =>
        comercio.Http.PutAsJsonAsync($"/api/productos/{producto.Id}", new
        {
            sku = producto.Sku, nombre = $"{producto.Nombre} editado", codigoBarras = (string?)null, categoriaId = producto.CategoriaId,
            precioCompra, porcentajeGanancia = 30m, precioVenta = (decimal?)null, porcentajeIva = 21m, venderSinStock = false, stockMinimo = 0m
        }, ApiTestHelpers.Json);

    private static byte[] Planilla(params object?[][] filas) =>
        LibroExcel.Escribir(new HojaExcel("Productos", PlanillaProductosExcel.Encabezados, filas));

    private static Task<HttpResponseMessage> ImportarAsync(ComercioDePrueba comercio, byte[] contenido, bool simular)
    {
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(ExportarProductosQueryHandler.TipoXlsx);
        var form = new MultipartFormDataContent { { archivo, "archivo", "productos.xlsx" } };
        return comercio.Http.PostAsync($"/api/productos/importar?simular={(simular ? "true" : "false")}", form);
    }
}
