using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public enum FormaPagoCompra
{
    Efectivo = 1,
    Transferencia = 2,
    CuentaCorrienteProveedor = 3
}

public sealed record ProveedorDto(
    Guid Id,
    string RazonSocial,
    string Cuit,
    string? Telefono,
    string? Email,
    string? Direccion,
    bool EstaActivo)
{
    /// <summary>CUIT con guiones: 30-71234567-9.</summary>
    public string CuitFormateado => Cuit.Length == 11 ? $"{Cuit[..2]}-{Cuit[2..10]}-{Cuit[10..]}" : Cuit;
}

public sealed class ProveedorFormulario
{
    [Required(ErrorMessage = "Ingresá la razón social.")]
    [StringLength(150)]
    public string RazonSocial { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá el CUIT.")]
    [RegularExpression(@"^\d{2}-?\d{8}-?\d$", ErrorMessage = "El CUIT debe tener 11 dígitos (con o sin guiones).")]
    public string Cuit { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El email no es válido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(200)]
    public string? Direccion { get; set; }

    public static ProveedorFormulario Desde(ProveedorDto p) => new()
    {
        RazonSocial = p.RazonSocial,
        Cuit = p.CuitFormateado,
        Telefono = p.Telefono,
        Email = p.Email,
        Direccion = p.Direccion
    };
}

public sealed record CompraDetalleDto(Guid ProductoId, string Producto, decimal Cantidad, decimal CostoUnitario, decimal Subtotal);

public sealed record CompraDto(
    Guid Id,
    Guid ProveedorId,
    string Proveedor,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    decimal MontoTotal,
    Guid? CajaSesionId,
    DateTime FechaRegistroUtc,
    string? Observaciones,
    IReadOnlyList<CompraDetalleDto> Detalles);

public sealed record CompraResumenDto(
    Guid Id,
    Guid ProveedorId,
    string Proveedor,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    decimal MontoTotal,
    int CantidadItems,
    bool ImpactoEnCaja);

public sealed record ItemCompraRequest(Guid ProductoId, decimal Cantidad, decimal CostoUnitario);

public sealed record RegistrarCompraRequest(
    Guid ProveedorId,
    string NumeroComprobante,
    DateOnly FechaCompra,
    FormaPagoCompra FormaPago,
    Guid? CajaSesionId,
    string? Observaciones,
    IReadOnlyList<ItemCompraRequest> Items);

/// <summary>Renglón editable del formulario de compra.</summary>
public sealed class RenglonCompra(ProductoDto producto)
{
    public ProductoDto Producto { get; } = producto;
    public decimal Cantidad { get; set; } = 1;
    public decimal CostoUnitario { get; set; } = producto.PrecioCompra;
    public decimal Subtotal => decimal.Round(Cantidad * CostoUnitario, 2, MidpointRounding.AwayFromZero);
}

public static class EtiquetasCompra
{
    public static string FormaPago(FormaPagoCompra forma) => forma switch
    {
        FormaPagoCompra.CuentaCorrienteProveedor => "Cuenta corriente proveedor",
        _ => forma.ToString()
    };
}
