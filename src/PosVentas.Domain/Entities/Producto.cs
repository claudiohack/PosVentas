using PosVentas.Domain.Common;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Artículo vendible. El precio de venta es neto (sin IVA) y se calcula a partir del costo y el margen.
/// El stock solo cambia mediante <see cref="IngresarStock"/> y <see cref="DescontarStock"/>.
/// </summary>
public sealed class Producto : TenantEntity
{
    public const int LongitudMaximaNombre = 150;

    public Sku Sku { get; private set; } = null!;
    public string Nombre { get; private set; } = string.Empty;
    public Money PrecioCompra { get; private set; } = null!;
    public decimal PorcentajeGanancia { get; private set; }
    public Money PrecioVenta { get; private set; } = null!;
    public decimal PorcentajeIva { get; private set; }
    public decimal StockActual { get; private set; }
    public decimal StockMinimo { get; private set; }
    public bool VenderSinStock { get; private set; }
    public bool EstaActivo { get; private set; }

    public Guid CategoriaId { get; private set; }
    public Categoria? Categoria { get; private set; }

    /// <summary>Concurrencia optimista: evita que dos cajas descuenten el mismo stock a la vez.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private Producto(Guid id) : base(id) { }

    // Requerido por EF Core
    private Producto() { }

    public static Producto Crear(
        Sku sku,
        string nombre,
        Money precioCompra,
        decimal porcentajeGanancia,
        decimal porcentajeIva,
        Guid categoriaId,
        bool venderSinStock,
        decimal stockMinimo = 0)
    {
        var producto = new Producto(Guid.NewGuid()) { EstaActivo = true, StockActual = 0 };
        producto.ConfigurarStockMinimo(stockMinimo);
        producto.ActualizarDatosBasicos(sku, nombre, porcentajeIva);
        producto.AsignarPreciosYMargen(precioCompra, porcentajeGanancia);
        producto.CambiarCategoria(categoriaId);
        producto.ConfigurarVentaSinStock(venderSinStock);
        return producto;
    }

    public void ActualizarDatosBasicos(Sku sku, string nombre, decimal porcentajeIva)
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (string.IsNullOrWhiteSpace(nombre))
            throw new DomainException("El nombre del producto es obligatorio.");
        if (porcentajeIva is < 0 or > 100)
            throw new DomainException("El IVA debe estar entre 0 y 100%.");

        Sku = sku;
        Nombre = nombre.Trim();
        PorcentajeIva = porcentajeIva;
        MarcarModificado();
    }

    public void AsignarPreciosYMargen(Money precioCompra, decimal porcentajeGanancia)
    {
        ArgumentNullException.ThrowIfNull(precioCompra);
        if (porcentajeGanancia < 0)
            throw new DomainException("El porcentaje de ganancia no puede ser negativo.");

        PrecioCompra = precioCompra;
        PorcentajeGanancia = porcentajeGanancia;
        PrecioVenta = precioCompra.Multiplicar(1 + porcentajeGanancia / 100m);
        MarcarModificado();
    }

    public void CambiarCategoria(Guid categoriaId)
    {
        if (categoriaId == Guid.Empty)
            throw new DomainException("La categoría es obligatoria.");
        CategoriaId = categoriaId;
        MarcarModificado();
    }

    public void ConfigurarVentaSinStock(bool permitir)
    {
        VenderSinStock = permitir;
        MarcarModificado();
    }

    /// <summary>Ajuste manual de existencias (sin costo asociado).</summary>
    public void IngresarStock(decimal cantidad)
    {
        if (cantidad <= 0)
            throw new DomainException("La cantidad a ingresar debe ser mayor a cero.");

        StockActual += cantidad;
        MarcarModificado();
    }

    /// <summary>
    /// Ingreso por compra: suma existencias y actualiza el costo por promedio ponderado
    /// ((stock × costo actual + cantidad × costo nuevo) / stock total). Si no había stock positivo,
    /// el costo pasa a ser directamente el de la compra. El precio de venta se recalcula
    /// manteniendo el margen de ganancia configurado.
    /// </summary>
    public void IngresarStock(decimal cantidad, decimal nuevoCosto)
    {
        if (cantidad <= 0)
            throw new DomainException("La cantidad a ingresar debe ser mayor a cero.");
        if (nuevoCosto < 0)
            throw new DomainException("El costo unitario no puede ser negativo.");

        var costoPromedio = StockActual > 0
            ? (StockActual * PrecioCompra.Monto + cantidad * nuevoCosto) / (StockActual + cantidad)
            : nuevoCosto;

        StockActual += cantidad;
        AsignarPreciosYMargen(Money.Crear(costoPromedio, PrecioCompra.Moneda), PorcentajeGanancia);
    }

    /// <summary>Umbral de alerta: el producto figura con stock bajo cuando StockActual &lt;= StockMinimo.</summary>
    public void ConfigurarStockMinimo(decimal stockMinimo)
    {
        if (stockMinimo < 0)
            throw new DomainException("El stock mínimo no puede ser negativo.");
        StockMinimo = stockMinimo;
        MarcarModificado();
    }

    public bool TieneStockBajo => StockActual <= StockMinimo;

    public void DescontarStock(decimal cantidad)
    {
        if (cantidad <= 0)
            throw new DomainException("La cantidad a descontar debe ser mayor a cero.");
        if (!EstaActivo)
            throw new DomainException($"El producto '{Nombre}' está dado de baja.");
        if (StockActual < cantidad && !VenderSinStock)
            throw new DomainException($"Stock insuficiente para '{Nombre}'. Disponible: {StockActual}, solicitado: {cantidad}.");

        StockActual -= cantidad;
        MarcarModificado();
    }

    public void Desactivar()
    {
        EstaActivo = false;
        MarcarModificado();
    }

    public void Activar()
    {
        EstaActivo = true;
        MarcarModificado();
    }
}
