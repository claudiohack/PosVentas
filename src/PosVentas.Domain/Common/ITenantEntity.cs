namespace PosVentas.Domain.Common;

/// <summary>
/// Marca una entidad como perteneciente a un comercio (tenant).
/// El TenantId lo asigna la infraestructura al persistir y se filtra en todas las consultas.
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

/// <summary>
/// Base para todas las entidades de negocio aisladas por comercio.
/// </summary>
public abstract class TenantEntity : Entity<Guid>, ITenantEntity
{
    public Guid TenantId { get; set; }

    protected TenantEntity(Guid id) : base(id) { }

    // Requerido por EF Core
    protected TenantEntity() { }
}
