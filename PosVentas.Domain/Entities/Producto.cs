using System;
using PosVentas.Domain.Common;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Domain.Entities;

public class Producto : Entity<Guid>
{
    public Sku Sku { get; private set; } = null!;
    public string Nombre { get; private set; } = string.Empty;

    // --- NUEVOS CAMPOS PARA RENTABILIDAD ---
    public Money PrecioCompra { get; private set; } = null!;
    public decimal PorcentajeGanancia { get; private set; }
    // ----------------------------------------

    public Money Precio { get; private set; } = null!; // Este sigue siendo el Precio de Venta
    public decimal PorcentajeIva { get; private set; }
    public int StockActual { get; private set; }
    public bool VenderSinStock { get; private set; }
    public Guid CategoriaId { get; private set; }
    public Categoria Categoria { get; private set; } = null!;

    // Constructor actualizado con los nuevos parámetros obligatorios
    public Producto(Guid id, Sku sku, string nombre, Money precioCompra, decimal porcentajeGanancia, decimal porcentajeIva, Guid categoriaId, bool venderSinStock = false) : base(id)
    {
        ActualizarDatosBasicos(sku, nombre, porcentajeIva);
        AsignarPreciosYMargen(precioCompra, porcentajeGanancia);
        CambiarCategoria(categoriaId);
        ConfigurarVentaSinStock(venderSinStock);
        StockActual = 0;
    }

#pragma warning disable CS8618
    protected Producto() { }
#pragma warning restore CS8618

    public void ActualizarDatosBasicos(Sku sku, string nombre, decimal porcentajeIva)
    {
        Sku = sku ?? throw new ArgumentNullException(nameof(sku));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre no puede estar vacío.");

        Nombre = nombre.Trim();

        if (porcentajeIva < 0 || porcentajeIva > 100)
            throw new ArgumentOutOfRangeException(nameof(porcentajeIva), "El IVA debe estar entre 0 y 100%.");

        PorcentajeIva = porcentajeIva;
    }

    /// <summary>
    /// Establece el costo, el margen de ganancia y calcula automáticamente el precio de venta final.
    /// </summary>
    public void AsignarPreciosYMargen(Money precioCompra, decimal porcentajeGanancia)
    {
        PrecioCompra = precioCompra ?? throw new ArgumentNullException(nameof(precioCompra));

        if (porcentajeGanancia < 0)
            throw new ArgumentException("El porcentaje de ganancia no puede ser negativo.");

        PorcentajeGanancia = porcentajeGanancia;

        // Regla de Negocio Automatizada: Calculamos el precio de venta en base al costo y margen
        decimal montoVenta = PrecioCompra.Monto * (1 + (PorcentajeGanancia / 100));

        // Asumimos que tu clase Money recibe el decimal en su constructor
        Precio = new Money(montoVenta);
    }

    public void CambiarCategoria(Guid nuevaCategoriaId) => CategoriaId = nuevaCategoriaId;
    public void ConfigurarVentaSinStock(bool permitir) => VenderSinStock = permitir;
}