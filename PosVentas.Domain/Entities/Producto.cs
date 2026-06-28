using PosVentas.Domain.Common;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Domain.Entities;

public class Producto : Entity<Guid>
{
    public Sku Sku { get; private set; } = null!;
    public string Nombre { get; private set; } = string.Empty;
    public Money Precio { get; private set; } = null!;
    public decimal PorcentajeIva { get; private set; }
    public int StockActual { get; private set; }
    public bool VenderSinStock { get; private set; }
    public Guid CategoriaId { get; private set; }
    public Categoria Categoria { get; private set; } = null!;

    public Producto(Guid id, Sku sku, string nombre, Money precio, decimal porcentajeIva, Guid categoriaId, bool venderSinStock = false) : base(id)
    {
        ActualizarDatosBasicos(sku, nombre, precio, porcentajeIva);
        CambiarCategoria(categoriaId);
        ConfigurarVentaSinStock(venderSinStock);
        StockActual = 0;
    }

#pragma warning disable CS8618
    protected Producto() { }
#pragma warning restore CS8618

    public void ActualizarDatosBasicos(Sku sku, string nombre, Money precio, decimal porcentajeIva)
    {
        Sku = sku ?? throw new ArgumentNullException(nameof(sku));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre no puede estar vacío.");
        Nombre = nombre.Trim();
        Precio = precio ?? throw new ArgumentNullException(nameof(precio));
        if (porcentajeIva < 0 || porcentajeIva > 100)
            throw new ArgumentOutOfRangeException(nameof(porcentajeIva));
        PorcentajeIva = porcentajeIva;
    }

    public void CambiarCategoria(Guid nuevaCategoriaId) => CategoriaId = nuevaCategoriaId;
    public void ConfigurarVentaSinStock(bool permitir) => VenderSinStock = permitir;
}