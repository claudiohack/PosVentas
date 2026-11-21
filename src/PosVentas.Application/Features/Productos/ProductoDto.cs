namespace PosVentas.Application.Features.Productos;

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
