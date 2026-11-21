using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public sealed record CategoriaDto(Guid Id, string Nombre, string? Descripcion, int OrdenDisplay, bool EstaActivo);

public sealed class CategoriaFormulario
{
    [Required(ErrorMessage = "Ingresá el nombre.")]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Descripcion { get; set; }

    [Range(0, 100_000)]
    public int OrdenDisplay { get; set; }
}

/// <summary>Formulario de alta / edición de producto. El precio sale del margen, o se fija y el margen se recalcula.</summary>
public sealed class ProductoFormulario
{
    [Required(ErrorMessage = "Ingresá el SKU.")]
    [StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá el nombre.")]
    [StringLength(150)]
    public string Nombre { get; set; } = string.Empty;

    [RegularExpression("^[A-Za-z0-9]*$", ErrorMessage = "Solo letras y números, sin espacios.")]
    [StringLength(32)]
    public string? CodigoBarras { get; set; }

    public Guid CategoriaId { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "El costo no puede ser negativo.")]
    public decimal PrecioCompra { get; set; }

    [Range(0, 10_000, ErrorMessage = "El margen debe estar entre 0 y 10.000 %.")]
    public decimal PorcentajeGanancia { get; set; } = 30m;

    /// <summary>true = se fija el precio de venta y el margen se recalcula.</summary>
    public bool FijarPrecio { get; set; }

    public decimal PrecioVenta { get; set; }

    public decimal PorcentajeIva { get; set; } = 21m;
    public bool VenderSinStock { get; set; }

    [Range(0, 1_000_000)]
    public decimal StockMinimo { get; set; }

    [Range(0, 1_000_000)]
    public decimal StockInicial { get; set; }

    public static readonly decimal[] AlicuotasIva = [0m, 2.5m, 5m, 10.5m, 21m, 27m];

    /// <summary>Precio neto (sin IVA) que resulta del margen, para la vista previa.</summary>
    public decimal PrecioPorMargen => decimal.Round(PrecioCompra * (1 + PorcentajeGanancia / 100m), 2, MidpointRounding.AwayFromZero);

    public decimal PrecioNeto => FijarPrecio ? PrecioVenta : PrecioPorMargen;
    public decimal PrecioFinal => decimal.Round(PrecioNeto * (1 + PorcentajeIva / 100m), 2, MidpointRounding.AwayFromZero);
    public decimal? MargenResultante => PrecioCompra > 0 ? decimal.Round((PrecioNeto / PrecioCompra - 1) * 100m, 2) : null;

    public static ProductoFormulario Desde(ProductoDto p) => new()
    {
        Sku = p.Sku,
        Nombre = p.Nombre,
        CodigoBarras = p.CodigoBarras,
        CategoriaId = p.CategoriaId,
        PrecioCompra = p.PrecioCompra,
        PorcentajeGanancia = p.PorcentajeGanancia,
        PrecioVenta = p.PrecioVenta,
        PorcentajeIva = p.PorcentajeIva,
        VenderSinStock = p.VenderSinStock,
        StockMinimo = p.StockMinimo
    };
}

public sealed record InvitacionDto(
    Guid Id, string? EmailDestinatario, string? Nota, DateTime FechaCreacionUtc, DateTime VenceUtc, string Estado,
    DateTime? UsadaUtc, Guid? ComercioCreadoId);

public sealed record InvitacionCreadaDto(Guid Id, string Token, DateTime VenceUtc, string? EmailDestinatario);

public sealed class RegistroComercioFormulario
{
    [Required(ErrorMessage = "Pegá el código de invitación.")]
    public string TokenInvitacion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá el nombre del comercio.")]
    [StringLength(150)]
    public string NombreComercio { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elegí un identificador.")]
    [RegularExpression("^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$", ErrorMessage = "De 3 a 50 caracteres: minúsculas, números y guiones.")]
    public string Slug { get; set; } = string.Empty;

    [RegularExpression(@"^\d{2}-?\d{8}-?\d$", ErrorMessage = "El CUIT debe tener 11 dígitos.")]
    public string? Cuit { get; set; }

    [Required(ErrorMessage = "Ingresá tu nombre.")]
    [StringLength(150)]
    public string NombreAdministrador { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elegí un usuario.")]
    [RegularExpression("^[a-zA-Z0-9._-]{3,50}$", ErrorMessage = "De 3 a 50 caracteres: letras, números, punto, guion o guion bajo.")]
    public string Username { get; set; } = "admin";

    [Required(ErrorMessage = "Elegí una contraseña.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Al menos 10 caracteres, con letras y números.")]
    public string Password { get; set; } = string.Empty;
}
