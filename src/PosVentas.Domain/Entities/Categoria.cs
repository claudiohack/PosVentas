using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public sealed class Categoria : TenantEntity
{
    public const int LongitudMaximaNombre = 100;
    public const int LongitudMaximaDescripcion = 250;

    public string Nombre { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }
    public int OrdenDisplay { get; private set; }
    public bool EstaActivo { get; private set; }

    private Categoria(Guid id) : base(id) { }

    // Requerido por EF Core
    private Categoria() { }

    public static Categoria Crear(string nombre, string? descripcion, int ordenDisplay)
    {
        var categoria = new Categoria(Guid.NewGuid()) { EstaActivo = true };
        categoria.ModificarDatos(nombre, descripcion);
        categoria.CambiarOrden(ordenDisplay);
        return categoria;
    }

    public void ModificarDatos(string nombre, string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new DomainException("El nombre de la categoría es obligatorio.");

        Nombre = nombre.Trim();
        Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        MarcarModificado();
    }

    public void CambiarOrden(int nuevoOrden)
    {
        if (nuevoOrden < 0)
            throw new DomainException("El orden no puede ser negativo.");
        OrdenDisplay = nuevoOrden;
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
