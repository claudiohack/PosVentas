using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Caja física (punto de cobro). Sus turnos se registran en <see cref="CajaSesion"/>.
/// </summary>
public sealed class Caja : TenantEntity
{
    public const int LongitudMaximaNombre = 100;

    public string Nombre { get; private set; } = string.Empty;
    public bool EstaAbierta { get; private set; }
    public bool EstaActivo { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Caja(Guid id) : base(id) { }

    // Requerido por EF Core
    private Caja() { }

    public static Caja Crear(string nombre)
    {
        var caja = new Caja(Guid.NewGuid()) { EstaActivo = true, EstaAbierta = false };
        caja.Renombrar(nombre);
        return caja;
    }

    public void Renombrar(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new DomainException("El nombre de la caja es obligatorio.");
        Nombre = nombre.Trim();
        MarcarModificado();
    }

    internal void MarcarComoAbierta()
    {
        if (!EstaActivo)
            throw new DomainException($"La caja '{Nombre}' está dada de baja.");
        if (EstaAbierta)
            throw new DomainException($"La caja '{Nombre}' ya tiene un turno abierto.");
        EstaAbierta = true;
        MarcarModificado();
    }

    internal void MarcarComoCerrada()
    {
        if (!EstaAbierta)
            throw new DomainException($"La caja '{Nombre}' ya está cerrada.");
        EstaAbierta = false;
        MarcarModificado();
    }

    public void Desactivar()
    {
        if (EstaAbierta)
            throw new DomainException("No se puede dar de baja una caja con un turno abierto.");
        EstaActivo = false;
        MarcarModificado();
    }

    public void Activar()
    {
        EstaActivo = true;
        MarcarModificado();
    }
}
