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

    // ---------- Infraestructura ----------

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(metodo, ruta);
        if (sesion.Actual is { } actual)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actual.AccessToken);
        if (cuerpo is not null)
            request.Content = JsonContent.Create(cuerpo, cuerpo.GetType(), options: Json);

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
            _ when problema?.Errors is { Count: > 0 } errores => string.Join(" ", errores.Values.SelectMany(e => e).Distinct()),
            _ => problema?.Detail ?? problema?.Title ?? $"Error {(int)respuesta.StatusCode} al comunicarse con el servidor."
        };
        throw new ApiException(mensaje, respuesta.StatusCode);
    }
}
