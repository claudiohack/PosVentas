using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class Categoria : Entity<Guid>
{
    public string Nombre { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public int OrdenDisplay { get; private set; }

    private readonly List<Producto> _productos = [];
    public IReadOnlyCollection<Producto> Productos => _productos.AsReadOnly();

    public Categoria(Guid id, string nombre, string descripcion, int ordenDisplay) : base(id)
    {
        ModificarDatos(nombre, descripcion);
        CambiarOrden(ordenDisplay);
    }

#pragma warning disable CS8618
    protected Categoria() { }
#pragma warning restore CS8618

    public void ModificarDatos(string nombre, string descripcion)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la categoría no puede estar vacío.");

        Nombre = nombre.Trim();
        Descripcion = descripcion?.Trim() ?? string.Empty;
    }

    public void CambiarOrden(int nuevoOrden)
    {
        if (nuevoOrden < 0)
            throw new ArgumentException("El orden no puede ser negativo.");
        OrdenDisplay = nuevoOrden;
    }
}