using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PosVentas.Client.Models;

namespace PosVentas.Client.Services;

public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>Venta que quedó registrada pendiente de facturación (rechazo de ARCA o ARCA no disponible).</summary>
    public Guid? VentaPendienteId { get; init; }

    public IReadOnlyList<ErrorAfipDto> ErroresAfip { get; init; } = [];
}

/// <summary>
/// Cliente tipado de la WebAPI. Agrega el JWT de la sesión a cada request autenticado.
/// </summary>
public sealed class PosVentasApi(HttpClient http, SesionService sesion)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ---------- Auth ----------

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync("api/auth/login", request, Json, ct);
        return await LeerAsync<AuthResponse>(respuesta, ct);
    }

    public async Task<AuthResponse> RegistrarComercioAsync(RegistroComercioFormulario f, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync("api/auth/registro-invitacion", f, Json, ct);
        return await LeerAsync<AuthResponse>(respuesta, ct);
    }

    // ---------- Invitaciones (administradores de la plataforma) ----------

    public Task<List<InvitacionDto>> ObtenerInvitacionesAsync(CancellationToken ct = default) =>
        EnviarAsync<List<InvitacionDto>>(HttpMethod.Get, "api/auth/invitaciones", null, ct);

    public Task<InvitacionCreadaDto> CrearInvitacionAsync(string? emailDestinatario, string? nota, int diasVigencia, CancellationToken ct = default) =>
        EnviarAsync<InvitacionCreadaDto>(HttpMethod.Post, "api/auth/invitaciones", new { emailDestinatario, nota, diasVigencia }, ct);

    public Task<InvitacionDto> RevocarInvitacionAsync(Guid id, CancellationToken ct = default) =>
        EnviarAsync<InvitacionDto>(HttpMethod.Delete, $"api/auth/invitaciones/{id}", null, ct);

    // ---------- Categorías ----------

    public Task<List<CategoriaDto>> ObtenerCategoriasAsync(bool incluirInactivas = false, CancellationToken ct = default) =>
        EnviarAsync<List<CategoriaDto>>(HttpMethod.Get, $"api/categorias{(incluirInactivas ? "?incluirInactivas=true" : null)}", null, ct);

    public async Task<Guid> CrearCategoriaAsync(CategoriaFormulario f, CancellationToken ct = default) =>
        (await EnviarAsync<IdCreado>(HttpMethod.Post, "api/categorias", f, ct)).Id;

    public Task<CategoriaDto> ActualizarCategoriaAsync(Guid id, CategoriaFormulario f, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Put, $"api/categorias/{id}", f, ct);

    public Task<CategoriaDto> CambiarEstadoCategoriaAsync(Guid id, bool activa, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Patch, $"api/categorias/{id}/estado", new { activa }, ct);

    public Task<List<CategoriaDto>> ReordenarCategoriasAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        EnviarAsync<List<CategoriaDto>>(HttpMethod.Put, "api/categorias/orden", new { ids }, ct);

    private sealed record IdCreado(Guid Id);

    // ---------- Productos ----------

    public Task<ProductoDto> CrearProductoAsync(ProductoFormulario f, CancellationToken ct = default) =>
        EnviarAsync<ProductoDto>(HttpMethod.Post, "api/productos", new
        {
            f.Sku, f.Nombre, f.CodigoBarras, f.PrecioCompra, moneda = "ARS", f.PorcentajeGanancia,
            precioVenta = f.FijarPrecio ? f.PrecioVenta : (decimal?)null, f.PorcentajeIva, f.CategoriaId,
            f.VenderSinStock, f.StockMinimo, f.StockInicial
        }, ct);

    public Task<ProductoDto> ActualizarProductoAsync(Guid id, ProductoFormulario f, CancellationToken ct = default) =>
        EnviarAsync<ProductoDto>(HttpMethod.Put, $"api/productos/{id}", new
        {
            f.Sku, f.Nombre, f.CodigoBarras, f.CategoriaId, f.PrecioCompra, f.PorcentajeGanancia,
            precioVenta = f.FijarPrecio ? f.PrecioVenta : (decimal?)null, f.PorcentajeIva, f.VenderSinStock, f.StockMinimo
        }, ct);

    public Task<ProductoDto> CambiarEstadoProductoAsync(Guid id, bool activo, CancellationToken ct = default) =>
        EnviarAsync<ProductoDto>(HttpMethod.Patch, $"api/productos/{id}/estado", new { activo }, ct);

    public Task<List<ProductoDto>> ObtenerProductosTodosAsync(string? buscar, bool incluirInactivos, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(buscar)) query.Add($"buscar={Uri.EscapeDataString(buscar)}");
        if (incluirInactivos) query.Add("incluirInactivos=true");
        return EnviarAsync<List<ProductoDto>>(HttpMethod.Get, query.Count == 0 ? "api/productos" : $"api/productos?{string.Join('&', query)}", null, ct);
    }

    public Task<List<ProductoDto>> ObtenerProductosAsync(string? buscar, CancellationToken ct = default)
    {
        var ruta = string.IsNullOrWhiteSpace(buscar)
            ? "api/productos"
            : $"api/productos?buscar={Uri.EscapeDataString(buscar)}";
        return EnviarAsync<List<ProductoDto>>(HttpMethod.Get, ruta, null, ct);
    }

    /// <summary>Catálogo completo en .xlsx (sirve también de plantilla para importar).</summary>
    public async Task<byte[]> ExportarProductosAsync(CancellationToken ct = default)
    {
        using var request = CrearRequest(HttpMethod.Get, "api/productos/exportar", null);
        using var respuesta = await http.SendAsync(request, ct);
        if (!respuesta.IsSuccessStatusCode)
            await LeerAsync<object>(respuesta, ct); // lanza ApiException con el ProblemDetails
        return await respuesta.Content.ReadAsByteArrayAsync(ct);
    }

    /// <param name="simular">true = vista previa sin guardar.</param>
    public async Task<ResultadoImportacionDto> ImportarProductosAsync(byte[] contenido, string nombreArchivo, bool simular, CancellationToken ct = default)
    {
        using var request = CrearRequest(HttpMethod.Post, $"api/productos/importar?simular={(simular ? "true" : "false")}", null);
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        request.Content = new MultipartFormDataContent { { archivo, "archivo", nombreArchivo } };
        using var respuesta = await http.SendAsync(request, ct);
        return await LeerAsync<ResultadoImportacionDto>(respuesta, ct);
    }

    public Task<List<ProductoDto>> ObtenerProductosStockBajoAsync(CancellationToken ct = default) =>
        EnviarAsync<List<ProductoDto>>(HttpMethod.Get, "api/productos/stock-bajo", null, ct);

    public Task<ProductoDto> ConfigurarStockMinimoAsync(Guid productoId, decimal stockMinimo, CancellationToken ct = default) =>
        EnviarAsync<ProductoDto>(HttpMethod.Put, $"api/productos/{productoId}/stock-minimo", new { stockMinimo }, ct);

    // ---------- Proveedores ----------

    public Task<List<ProveedorDto>> ObtenerProveedoresAsync(string? buscar = null, bool incluirInactivos = false, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(buscar)) query.Add($"buscar={Uri.EscapeDataString(buscar)}");
        if (incluirInactivos) query.Add("incluirInactivos=true");
        var ruta = query.Count == 0 ? "api/proveedores" : $"api/proveedores?{string.Join('&', query)}";
        return EnviarAsync<List<ProveedorDto>>(HttpMethod.Get, ruta, null, ct);
    }

    public Task<ProveedorDto> CrearProveedorAsync(ProveedorFormulario f, CancellationToken ct = default) =>
        EnviarAsync<ProveedorDto>(HttpMethod.Post, "api/proveedores", f, ct);

    public Task<ProveedorDto> ActualizarProveedorAsync(Guid id, ProveedorFormulario f, CancellationToken ct = default) =>
        EnviarAsync<ProveedorDto>(HttpMethod.Put, $"api/proveedores/{id}", f, ct);

    public Task<ProveedorDto> CambiarEstadoProveedorAsync(Guid id, bool activo, CancellationToken ct = default) =>
        EnviarAsync<ProveedorDto>(HttpMethod.Patch, $"api/proveedores/{id}/estado", new { activo }, ct);

    // ---------- Compras ----------

    public Task<List<CompraResumenDto>> ObtenerComprasAsync(Guid? proveedorId = null, CancellationToken ct = default) =>
        EnviarAsync<List<CompraResumenDto>>(HttpMethod.Get,
            proveedorId is { } id ? $"api/compras?proveedorId={id}" : "api/compras", null, ct);

    public Task<CompraDto> RegistrarCompraAsync(RegistrarCompraRequest request, CancellationToken ct = default) =>
        EnviarAsync<CompraDto>(HttpMethod.Post, "api/compras", request, ct);

    // ---------- Caja ----------

    public Task<List<CajaDto>> ObtenerCajasAsync(CancellationToken ct = default) =>
        EnviarAsync<List<CajaDto>>(HttpMethod.Get, "api/cajas", null, ct);

    public Task<SesionCajaDto> AbrirCajaAsync(Guid cajaId, decimal montoApertura, CancellationToken ct = default) =>
        EnviarAsync<SesionCajaDto>(HttpMethod.Post, $"api/cajas/{cajaId}/apertura", new { montoApertura }, ct);

    public Task<ResumenSesionDto> ObtenerResumenAsync(Guid sesionId, CancellationToken ct = default) =>
        EnviarAsync<ResumenSesionDto>(HttpMethod.Get, $"api/cajas/sesiones/{sesionId}/resumen", null, ct);

    public Task<ResumenSesionDto> CerrarCajaAsync(Guid sesionId, decimal montoCierreReal, string? observaciones, CancellationToken ct = default) =>
        EnviarAsync<ResumenSesionDto>(HttpMethod.Post, $"api/cajas/sesiones/{sesionId}/cierre", new { montoCierreReal, observaciones }, ct);

    // ---------- Ventas ----------

    public Task<VentaDto> RegistrarVentaAsync(RegistrarVentaRequest request, CancellationToken ct = default) =>
        EnviarAsync<VentaDto>(HttpMethod.Post, "api/ventas", request, ct);

    // ---------- Clientes y cuentas corrientes ----------

    public Task<List<ClienteDto>> ObtenerClientesAsync(string? buscar = null, bool soloCuentaCorriente = false, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(buscar)) query.Add($"buscar={Uri.EscapeDataString(buscar)}");
        if (soloCuentaCorriente) query.Add("soloCuentaCorriente=true");
        var ruta = query.Count == 0 ? "api/clientes" : $"api/clientes?{string.Join('&', query)}";
        return EnviarAsync<List<ClienteDto>>(HttpMethod.Get, ruta, null, ct);
    }

    public Task<ClienteDto> CrearClienteAsync(NuevoClienteRequest request, CancellationToken ct = default) =>
        EnviarAsync<ClienteDto>(HttpMethod.Post, "api/clientes", request, ct);

    public Task<ClienteDto> ActualizarCreditoAsync(Guid clienteId, bool permiteCuentaCorriente, decimal limiteCredito, CancellationToken ct = default) =>
        EnviarAsync<ClienteDto>(HttpMethod.Put, $"api/clientes/{clienteId}/credito", new { permiteCuentaCorriente, limiteCredito }, ct);

    public Task<CuentaCorrienteDto> ObtenerCuentaCorrienteAsync(Guid clienteId, CancellationToken ct = default) =>
        EnviarAsync<CuentaCorrienteDto>(HttpMethod.Get, $"api/clientes/{clienteId}/cuenta-corriente", null, ct);

    public Task<ReciboCobranzaDto> RegistrarCobranzaAsync(Guid clienteId, decimal monto, MedioPago medio, Guid cajaSesionId, string? observaciones, CancellationToken ct = default) =>
        EnviarAsync<ReciboCobranzaDto>(HttpMethod.Post, $"api/clientes/{clienteId}/cobranzas",
            new { monto, medio, cajaSesionId, observaciones }, ct);

    public Task<VentaDto> FacturarVentaAsync(Guid ventaId, string tipoComprobante, CancellationToken ct = default) =>
        EnviarAsync<VentaDto>(HttpMethod.Post, $"api/ventas/{ventaId}/facturar", new { tipoComprobante }, ct);

    public Task<ComprobanteImpresionDto> ObtenerComprobanteAsync(Guid ventaId, CancellationToken ct = default) =>
        EnviarAsync<ComprobanteImpresionDto>(HttpMethod.Get, $"api/ventas/{ventaId}/comprobante", null, ct);

    // ---------- Historial y anulaciones ----------

    public Task<List<VentaHistorialDto>> ObtenerHistorialVentasAsync(DateOnly desde, DateOnly hasta, EstadoVenta? estado, CancellationToken ct = default) =>
        EnviarAsync<List<VentaHistorialDto>>(HttpMethod.Get,
            $"api/ventas?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}{(estado is { } e ? $"&estado={e}" : null)}", null, ct);

    public Task<AnulacionDto> AnularVentaAsync(Guid ventaId, string motivoAnulacion, CancellationToken ct = default) =>
        EnviarAsync<AnulacionDto>(HttpMethod.Post, $"api/ventas/{ventaId}/anular", new { motivoAnulacion }, ct);

    public Task<AnulacionDto> ObtenerAnulacionAsync(Guid ventaId, CancellationToken ct = default) =>
        EnviarAsync<AnulacionDto>(HttpMethod.Get, $"api/ventas/{ventaId}/anulacion", null, ct);

    public Task<AnulacionDto> EmitirNotaCreditoAsync(Guid ventaId, CancellationToken ct = default) =>
        EnviarAsync<AnulacionDto>(HttpMethod.Post, $"api/ventas/{ventaId}/nota-credito", new { }, ct);

    public Task<ComprobanteImpresionDto> ObtenerNotaCreditoAsync(Guid ventaId, CancellationToken ct = default) =>
        EnviarAsync<ComprobanteImpresionDto>(HttpMethod.Get, $"api/ventas/{ventaId}/nota-credito", null, ct);

    // ---------- Reportes (administradores y supervisores) ----------

    private static string Periodo(DateOnly desde, DateOnly hasta) => $"desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}";

    public Task<DashboardResumenDto> ObtenerDashboardAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        EnviarAsync<DashboardResumenDto>(HttpMethod.Get, $"api/reportes/dashboard?{Periodo(desde, hasta)}", null, ct);

    public Task<TopProductosDto> ObtenerTopProductosAsync(DateOnly desde, DateOnly hasta, int top, string ordenarPor, CancellationToken ct = default) =>
        EnviarAsync<TopProductosDto>(HttpMethod.Get, $"api/reportes/top-productos?{Periodo(desde, hasta)}&top={top}&ordenarPor={ordenarPor}", null, ct);

    public Task<RentabilidadPorCategoriaDto> ObtenerRentabilidadPorCategoriaAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        EnviarAsync<RentabilidadPorCategoriaDto>(HttpMethod.Get, $"api/reportes/categorias?{Periodo(desde, hasta)}", null, ct);

    public Task<ReporteCierresCajaDto> ObtenerReporteCajasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        EnviarAsync<ReporteCierresCajaDto>(HttpMethod.Get, $"api/reportes/cajas?{Periodo(desde, hasta)}", null, ct);

    // ---------- Configuración fiscal (solo administradores) ----------

    public Task<ConfiguracionFiscalDto> ObtenerConfiguracionFiscalAsync(CancellationToken ct = default) =>
        EnviarAsync<ConfiguracionFiscalDto>(HttpMethod.Get, "api/comercio/configuracion-fiscal", null, ct);

    public Task<ConfiguracionFiscalDto> ActualizarDatosFiscalesAsync(DatosFiscalesFormulario f, CancellationToken ct = default) =>
        EnviarAsync<ConfiguracionFiscalDto>(HttpMethod.Put, "api/comercio/configuracion-fiscal", f, ct);

    public Task<ConfiguracionFiscalDto> CargarCertificadoPkcs12Async(byte[] contenido, string? password, CancellationToken ct = default) =>
        EnviarAsync<ConfiguracionFiscalDto>(HttpMethod.Put, "api/comercio/certificado-afip",
            new { formato = "Pkcs12", pkcs12Base64 = Convert.ToBase64String(contenido), password }, ct);

    public Task<ConfiguracionFiscalDto> CargarCertificadoPemAsync(string certificadoPem, string clavePrivadaPem, string? password, CancellationToken ct = default) =>
        EnviarAsync<ConfiguracionFiscalDto>(HttpMethod.Put, "api/comercio/certificado-afip",
            new { formato = "Pem", certificadoPem, clavePrivadaPem, password }, ct);

    public Task<EstadoServicioAfipDto> VerificarConexionAfipAsync(CancellationToken ct = default) =>
        EnviarAsync<EstadoServicioAfipDto>(HttpMethod.Post, "api/comercio/afip/verificacion", new { }, ct);

    // ---------- Usuarios (solo administradores) ----------

    public Task<List<UsuarioDto>> ObtenerUsuariosAsync(CancellationToken ct = default) =>
        EnviarAsync<List<UsuarioDto>>(HttpMethod.Get, "api/usuarios", null, ct);

    public Task<UsuarioDto> CrearUsuarioAsync(UsuarioFormulario f, CancellationToken ct = default) =>
        EnviarAsync<UsuarioDto>(HttpMethod.Post, "api/usuarios",
            new { f.NombreCompleto, f.NombreUsuario, f.Email, f.Password, f.Rol }, ct);

    public Task<UsuarioDto> ActualizarUsuarioAsync(Guid id, UsuarioFormulario f, CancellationToken ct = default) =>
        EnviarAsync<UsuarioDto>(HttpMethod.Put, $"api/usuarios/{id}", new { f.NombreCompleto, f.Email, f.Rol }, ct);

    public Task CambiarPasswordAsync(Guid id, string nuevaPassword, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Put, $"api/usuarios/{id}/password", new { nuevaPassword }, ct);

    public Task<UsuarioDto> CambiarEstadoUsuarioAsync(Guid id, bool activo, CancellationToken ct = default) =>
        EnviarAsync<UsuarioDto>(HttpMethod.Patch, $"api/usuarios/{id}/estado", new { activo }, ct);

    // ---------- Infraestructura ----------

    private async Task EnviarSinRespuestaAsync(HttpMethod metodo, string ruta, object cuerpo, CancellationToken ct)
    {
        using var request = CrearRequest(metodo, ruta, cuerpo);
        using var respuesta = await http.SendAsync(request, ct);
        if (!respuesta.IsSuccessStatusCode)
            await LeerAsync<object>(respuesta, ct); // lanza ApiException con el ProblemDetails
    }

    private HttpRequestMessage CrearRequest(HttpMethod metodo, string ruta, object? cuerpo)
    {
        var request = new HttpRequestMessage(metodo, ruta);
        if (sesion.Actual is { } actual)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actual.AccessToken);
        if (cuerpo is not null)
            request.Content = JsonContent.Create(cuerpo, cuerpo.GetType(), options: Json);
        return request;
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
    {
        using var request = CrearRequest(metodo, ruta, cuerpo);
        using var respuesta = await http.SendAsync(request, ct);
        return await LeerAsync<T>(respuesta, ct);
    }

    private async Task<T> LeerAsync<T>(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode)
            return (await respuesta.Content.ReadFromJsonAsync<T>(Json, ct))!;

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized && sesion.EstaAutenticado)
            sesion.Cerrar();

        ProblemaApi? problema = null;
        try
        {
            problema = await respuesta.Content.ReadFromJsonAsync<ProblemaApi>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // La respuesta no era ProblemDetails
        }

        var mensaje = respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tenés permisos para esta operación.",
            HttpStatusCode.TooManyRequests => "Demasiados intentos. Esperá un minuto y volvé a probar.",
            _ when problema?.ErroresAfip is { Count: > 0 } afip =>
                $"{problema.Title} " + string.Join(" · ", afip.Select(e => $"[{e.Codigo}] {e.Mensaje}")),
            _ when problema?.Errors is { Count: > 0 } errores => string.Join(" ", errores.Values.SelectMany(e => e).Distinct()),
            _ => problema?.Detail ?? problema?.Title ?? $"Error {(int)respuesta.StatusCode} al comunicarse con el servidor."
        };
        throw new ApiException(mensaje, respuesta.StatusCode)
        {
            VentaPendienteId = problema?.VentaId,
            ErroresAfip = problema?.ErroresAfip ?? []
        };
    }
}
