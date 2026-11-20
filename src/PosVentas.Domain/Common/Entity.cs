namespace PosVentas.Domain.Common;

/// <summary>
/// Entidad base con identidad y auditoría de fechas (UTC).
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>> where TId : notnull
{
    public TId Id { get; protected init; } = default!;
    public DateTime FechaCreacionUtc { get; protected init; } = DateTime.UtcNow;
    public DateTime? FechaModificacionUtc { get; protected set; }

    protected Entity(TId id)
    {
        Id = id;
    }

    // Requerido por EF Core
    protected Entity() { }

    protected void MarcarModificado() => FechaModificacionUtc = DateTime.UtcNow;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
