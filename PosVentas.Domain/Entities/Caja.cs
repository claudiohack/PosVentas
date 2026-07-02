using System;
using System.Collections.Generic;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class Caja : Entity<Guid>
{
    // --- DATOS BÁSICOS ---
    public string Nombre { get; private set; } = string.Empty; // Ej: "Caja Central", "Caja 01"
    public string? ComputadoraAsignada { get; private set; } // Nombre de red o IP opcional para auto-detectar

    // --- ESTADOS DE CONTROL ---
    public bool EstaAbierta { get; private set; } = false; // Indica si tiene un turno activo corriendo
    public bool EstaActivo { get; private set; } = true;   // Para baja lógica

    // --- AUDITORÍA Y CONCURRENCIA ---
    public DateTime? FechaModificacion { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    // Relación con sus sesiones / aperturas históricas
    private readonly List<CajaSesion> _sesiones = [];
    public IReadOnlyCollection<CajaSesion> Sesiones => _sesiones.AsReadOnly();

    // Constructor para ALTA de Cajas
    public Caja(Guid id, string nombre, string? computadoraAsignada) : base(id)
    {
        ModificarDatos(nombre, computadoraAsignada);
        EstaAbierta = false;
        EstaActivo = true;
    }

#pragma warning disable CS8618
    // Constructor requerido por Entity Framework Core
    protected Caja() { }
#pragma warning restore CS8618

    // --- MÉTODOS DE COMPORTAMIENTO SEGURO ---

    public void ModificarDatos(string nombre, string? computadoraAsignada)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la caja es obligatorio.");

        Nombre = nombre.Trim();
        ComputadoraAsignada = string.IsNullOrWhiteSpace(computadoraAsignada) ? null : computadoraAsignada.Trim();

        FechaModificacion = DateTime.UtcNow;
    }

    // El dominio controla estrictamente las transiciones de estado para evitar aperturas duplicadas
    public void MarcarComoAbierta()
    {
        if (EstaAbierta)
            throw new InvalidOperationException($"La caja '{Nombre}' ya se encuentra abierta en otra sesión activa.");

        EstaAbierta = true;
        FechaModificacion = DateTime.UtcNow;
    }

    public void MarcarComoCerrada()
    {
        if (!EstaAbierta)
            throw new InvalidOperationException($"La caja '{Nombre}' ya está cerrada.");

        EstaAbierta = false;
        FechaModificacion = DateTime.UtcNow;
    }

    public void Desactivar()
    {
        if (EstaAbierta)
            throw new InvalidOperationException("No se puede dar de baja una caja que actualmente está abierta. Primero debe cerrarse el turno.");

        EstaActivo = false;
        FechaModificacion = DateTime.UtcNow;
    }

    public void Activar()
    {
        EstaActivo = true;
        FechaModificacion = DateTime.UtcNow;
    }
}