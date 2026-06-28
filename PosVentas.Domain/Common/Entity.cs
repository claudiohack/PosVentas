namespace PosVentas.Domain.Common;

public abstract class Entity<TId> : IEquatable<Entity<TId>> where TId : notnull
{
    public TId Id { get; init; }
    public DateTime FechaCreacion { get; init; } = DateTime.UtcNow;

    protected Entity(TId id)
    {
        Id = id;
    }

#pragma warning disable CS8618 // Permitir constructor vacío para el ORM
    protected Entity() { }
#pragma warning restore CS8618

    public override bool Equals(object? obj) =>
        obj is Entity<TId> entity && Equals(entity);

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}