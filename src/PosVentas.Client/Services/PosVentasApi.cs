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

    // ---------- Productos ----------

    public Task<List<ProductoDto>> ObtenerProductosAsync(string? buscar, CancellationToken ct = default)
    {
        var ruta = string.IsNullOrWhiteSpace(buscar)
            ? "api/productos"
            : $"api/productos?buscar={Uri.EscapeDataString(buscar)}";
        return EnviarAsync<List<ProductoDto>>(HttpMethod.Get, ruta, null, ct);
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
