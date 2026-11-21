using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Ingresá el identificador del comercio.")]
    [StringLength(50)]
    public string Comercio { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá tu usuario.")]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiraUtc,
    Guid UsuarioId,
    string NombreCompleto,
    string Rol,
    Guid ComercioId,
    string Comercio,
    bool EsAdministradorPlataforma = false);

public sealed record ProductoDto(
    Guid Id,
    string Sku,
    string Nombre,
    Guid CategoriaId,
    string? Categoria,
    decimal PrecioCompra,
    decimal PorcentajeGanancia,
    decimal PrecioVenta,
    string Moneda,
    decimal PorcentajeIva,
    decimal StockActual,
    decimal StockMinimo,
    bool StockBajo,
    bool VenderSinStock,
    bool EstaActivo,
    string? CodigoBarras = null);

public sealed record ProblemaApi(
    string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors,
    Guid? VentaId = null, List<ErrorAfipDto>? ErroresAfip = null);

/// <summary>Código y mensaje de error devueltos por ARCA.</summary>
public sealed record ErrorAfipDto(int Codigo, string Mensaje);
