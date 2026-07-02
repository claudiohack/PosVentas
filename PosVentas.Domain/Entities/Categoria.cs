using System;
using System.Collections.Generic;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class Categoria : Entity<Guid>
{
    // --- CAMPOS ACTUALES ---
    public string Nombre { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public int OrdenDisplay { get; private set; }

    // --- CAMPOS DE ESCALAMIENTO Y AUDITORÍA ---
    public bool EstaActivo { get; private set; } = true;

    // NOTA DE ARQUITECTURA: Se eliminó la propiedad FechaCreacion de aquí 
    // porque ya está heredada nativamente desde Entity<Guid>.

    public DateTime? FechaModificacion { get; private set; }

    // Concurrencia optimista nativa para SQL Server (mapeado como rowversion / timestamp)
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    private readonly List<Producto> _productos = [];
    public IReadOnlyCollection<Producto> Productos => _productos.AsReadOnly();

    // Constructor para ALTA (Nuevos registros)
    public Categoria(Guid id, string nombre, string descripcion, int ordenDisplay) : base(id)
    {
        ModificarDatos(nombre, descripcion);
        CambiarOrden(ordenDisplay);
        EstaActivo = true;

        // NOTA: Si tu clase base requiere inicializar la fecha de creación en el constructor y te lo permite,
        // lo podés hacer asignando a la propiedad heredada, de lo contrario la base ya lo maneja internamente.
    }

#pragma warning disable CS8618
    protected Categoria() { }
#pragma warning restore CS8618

    // --- MÉTODOS DE NEGOCIO ---
    public void ModificarDatos(string nombre, string descripcion)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la categoría no puede estar vacío.");

        Nombre = nombre.Trim();
        Descripcion = descripcion?.Trim() ?? string.Empty;

        // Registramos la fecha de modificación de forma nativa en el dominio
        FechaModificacion = DateTime.UtcNow;
    }

    public void CambiarOrden(int nuevoOrden)
    {
        if (nuevoOrden < 0)
            throw new ArgumentException("El orden no puede ser negativo.");

        OrdenDisplay = nuevoOrden;
        FechaModificacion = DateTime.UtcNow;
    }

    // Métodos explícitos para controlar la baja lógica desde la capa de aplicación/UI
    public void Desactivar()
    {
        EstaActivo = false;
        FechaModificacion = DateTime.UtcNow;
    }

    public void Activar()
    {
        EstaActivo = true;
        FechaModificacion = DateTime.UtcNow;
    }
}