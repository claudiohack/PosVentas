using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PosVentas.Application.Features.Categorias;
using PosVentas.Application.Features.Productos;
using PosVentas.Infrastructure.Planillas;
using PosVentas.IntegrationTests.Infrastructure;

namespace PosVentas.IntegrationTests;

[Collection(ApiCollection.Nombre)]
public sealed class PlanillaProductosTests(PosVentasApiFactory factory)
{
    [Fact]
    public async Task Exportar_devuelve_un_xlsx_solo_con_los_productos_del_comercio()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        await a.CrearProductoOkAsync("EXP-A", precioCompra: 1000m, ganancia: 30m, stockInicial: 7m);
        await b.CrearProductoOkAsync("EXP-B");

        var respuesta = await a.Http.GetAsync("/api/productos/exportar");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(ExportarProductosQueryHandler.TipoXlsx, respuesta.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", respuesta.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        // La planilla exportada se puede volver a leer con el mismo formato de importación
        await using var archivo = await respuesta.Content.ReadAsStreamAsync();
        using var copia = new MemoryStream();
        await archivo.CopyToAsync(copia);
        copia.Position = 0;
        var fila = Assert.Single(new PlanillaProductosExcel().Leer(copia).Filas);
        Assert.Equal(("EXP-A", "General", 1000m, 30m, 1300m, 21m, 7m, true),
            (fila.Sku, fila.Categoria, fila.Costo, fila.Margen, fila.PrecioVenta, fila.Iva, fila.Stock, fila.Activo));

        // Un cajero no exporta
        var cajero = await factory.LoginConRolAsync(a, "cajero-exp", "Cajero");
        Assert.Equal(HttpStatusCode.Forbidden, (await cajero.GetAsync("/api/productos/exportar")).StatusCode);
    }

    [Fact]
    public async Task Importar_muestra_vista_previa_y_luego_crea_actualiza_y_crea_categorias()
    {
        var a = await factory.RegistrarComercioAsync();
        var existente = await a.CrearProductoOkAsync("IMP-1", precioCompra: 100m, ganancia: 50m, stockInicial: 4m);

        var planilla = Planilla(
            ["IMP-1", "Producto editado", null, null, 200m, null, null, null, null, null, 999m, null],               // costo nuevo: precio por margen
            ["imp-nuevo", "Producto nuevo", "7791234567890", "Importados", 100m, null, 180m, 10.5m, 5m, "No", 12m, "Sí"]); // precio fijo

        // Vista previa: informa pero no guarda
        var previa = await ImportarAsync(a.Http, planilla, simular: true);
        Assert.Equal((false, 2, 1, 1, 0), (previa.Aplicado, previa.FilasLeidas, previa.Nuevos, previa.Actualizados, previa.SinCambios));
        Assert.Equal(["Importados"], previa.CategoriasNuevas);
        Assert.Empty(previa.Errores);
        Assert.Equal("Producto IMP-1", (await Producto(a, existente.Id)).Nombre);
        Assert.Empty((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=IMP-NUEVO", ApiTestHelpers.Json))!);

        // Importación real
        var resultado = await ImportarAsync(a.Http, planilla, simular: false);
        Assert.True(resultado.Aplicado);

        var editado = await Producto(a, existente.Id);
        Assert.Equal(("Producto editado", 200m, 50m, 300m, 4m), (editado.Nombre, editado.PrecioCompra, editado.PorcentajeGanancia, editado.PrecioVenta, editado.StockActual));

        var nuevo = Assert.Single((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=IMP-NUEVO", ApiTestHelpers.Json))!);
        Assert.Equal(("IMP-NUEVO", "7791234567890", "Importados", 180m, 80m, 10.5m, 12m, 5m),
            (nuevo.Sku, nuevo.CodigoBarras, nuevo.Categoria, nuevo.PrecioVenta, nuevo.PorcentajeGanancia, nuevo.PorcentajeIva, nuevo.StockActual, nuevo.StockMinimo));
        Assert.Contains((await a.Http.GetFromJsonAsync<List<CategoriaDto>>("/api/categorias", ApiTestHelpers.Json))!, c => c.Nombre == "Importados");

        // Repetir la misma planilla no cambia nada (el stock de los existentes no se toca)
        var repetida = await ImportarAsync(a.Http, planilla, simular: false);
        Assert.Equal((true, 0, 0, 2), (repetida.Aplicado, repetida.Nuevos, repetida.Actualizados, repetida.SinCambios));
        Assert.Equal(12m, (await Producto(a, nuevo.Id)).StockActual);
    }

    [Fact]
    public async Task Importar_con_errores_no_guarda_ninguna_fila_e_informa_cada_error()
    {
        var a = await factory.RegistrarComercioAsync();
        await a.CrearProductoOkAsync("CON-CODIGO");
        var conCodigo = Assert.Single((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=CON-CODIGO", ApiTestHelpers.Json))!);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PutAsJsonAsync($"/api/productos/{conCodigo.Id}", new
        {
            sku = "CON-CODIGO", nombre = conCodigo.Nombre, codigoBarras = "7790000000001", categoriaId = await a.CategoriaGeneralAsync(),
            precioCompra = 1000m, porcentajeGanancia = 30m, precioVenta = (decimal?)null, porcentajeIva = 21m, venderSinStock = false, stockMinimo = 0m
        }, ApiTestHelpers.Json)).StatusCode);

        var planilla = Planilla(
            ["OK-1", "Válido", null, "General", 100m, 30m, null, 21m, null, null, null, null],      // fila 2
            ["MAL-1", "Costo inválido", null, "General", "abc", 30m, null, 21m, null, null, null, null], // fila 3
            ["OK-1", "Repetido", null, "General", 100m, 30m, null, 21m, null, null, null, null],    // fila 4
            ["SIN-CAT", "Sin categoría", null, null, 100m, 30m, null, 21m, null, null, null, null],   // fila 5
            ["IVA-MAL", "IVA inválido", null, "General", 100m, 30m, null, 19m, null, null, null, null], // fila 6
            ["COD-DUP", "Código ajeno", "7790000000001", "General", 100m, 30m, null, 21m, null, null, null, null]); // fila 7

        var resultado = await ImportarAsync(a.Http, planilla, simular: false);
        Assert.False(resultado.Aplicado);
        Assert.Equal([3, 4, 5, 6, 7], resultado.Errores.Select(e => e.Fila));
        Assert.Contains("Costo", resultado.Errores[0].Mensaje);
        Assert.Contains("repetido", resultado.Errores[1].Mensaje);
        Assert.Contains("categoría", resultado.Errores[2].Mensaje);
        Assert.Contains("IVA", resultado.Errores[3].Mensaje);
        Assert.Contains("CON-CODIGO", resultado.Errores[4].Mensaje);

        // Todo o nada: la fila válida tampoco se guardó
        Assert.Empty((await a.Http.GetFromJsonAsync<List<ProductoDto>>("/api/productos?buscar=OK-1", ApiTestHelpers.Json))!);
    }

    [Fact]
    public async Task Importar_es_por_comercio_solo_para_administradores_y_rechaza_archivos_invalidos()
    {
        var a = await factory.RegistrarComercioAsync();
        var b = await factory.RegistrarComercioAsync();
        var deB = await b.CrearProductoOkAsync("COMPARTIDO", precioCompra: 500m);

        // El mismo SKU en otro comercio no se pisa: en A se crea uno nuevo
        var resultado = await ImportarAsync(a.Http, Planilla(["COMPARTIDO", "De A", null, "General", 10m, 0m, null, 21m, null, null, null, null]), simular: false);
        Assert.Equal((true, 1, 0), (resultado.Aplicado, resultado.Nuevos, resultado.Actualizados));
        var enB = await Producto(b, deB.Id);
        Assert.Equal((deB.Nombre, 500m), (enB.Nombre, enB.PrecioCompra));

        // Supervisor y cajero no importan
        var supervisor = await factory.LoginConRolAsync(a, "super-imp", "Supervisor");
        Assert.Equal(HttpStatusCode.Forbidden, (await Subir(supervisor, Planilla(), "productos.xlsx", simular: true)).StatusCode);

        // Archivos inválidos → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await Subir(a.Http, "sku,nombre\nX,Y"u8.ToArray(), "productos.csv", simular: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Subir(a.Http, "no es un excel"u8.ToArray(), "productos.xlsx", simular: true)).StatusCode);
        var sinSku = LibroExcel.Escribir(new HojaExcel("Hoja1", ["Nombre", "Costo"], [["Algo", 10m]]));
        var respuesta = await Subir(a.Http, sinSku, "productos.xlsx", simular: true);
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("SKU", await respuesta.Content.ReadAsStringAsync());
    }

    private static byte[] Planilla(params object?[][] filas) =>
        LibroExcel.Escribir(new HojaExcel("Productos", PlanillaProductosExcel.Encabezados, filas));

    private static async Task<ResultadoImportacionDto> ImportarAsync(HttpClient http, byte[] planilla, bool simular)
    {
        var respuesta = await Subir(http, planilla, "productos.xlsx", simular);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<ResultadoImportacionDto>(ApiTestHelpers.Json))!;
    }

    private static Task<HttpResponseMessage> Subir(HttpClient http, byte[] contenido, string nombre, bool simular)
    {
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(ExportarProductosQueryHandler.TipoXlsx);
        var form = new MultipartFormDataContent { { archivo, "archivo", nombre } };
        return http.PostAsync($"/api/productos/importar?simular={(simular ? "true" : "false")}", form);
    }

    private static async Task<ProductoDto> Producto(ComercioDePrueba comercio, Guid id) =>
        (await comercio.Http.GetFromJsonAsync<ProductoDto>($"/api/productos/{id}", ApiTestHelpers.Json))!;
}
