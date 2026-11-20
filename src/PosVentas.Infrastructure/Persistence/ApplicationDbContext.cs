using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentTenantService currentTenant) : DbContext(options), IApplicationDbContext
{
    private static readonly MethodInfo ConfigurarTenantMethod =
        typeof(ApplicationDbContext).GetMethod(nameof(ConfigurarEntidadTenant), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<CajaSesion> CajaSesiones => Set<CajaSesion>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<VentaDetalle> VentaDetalles => Set<VentaDetalle>();
    public DbSet<VentaPago> VentaPagos => Set<VentaPago>();
    public DbSet<MovimientoCaja> MovimientosCaja => Set<MovimientoCaja>();
    public DbSet<MovimientoCuentaCorriente> MovimientosCuentaCorriente => Set<MovimientoCuentaCorriente>();
    public DbSet<ReciboCobranza> RecibosCobranza => Set<ReciboCobranza>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<CompraDetalle> CompraDetalles => Set<CompraDetalle>();
    public DbSet<ComprobanteFiscal> ComprobantesFiscales => Set<ComprobanteFiscal>();

    /// <summary>
    /// Tenant del request. EF Core lo evalúa en cada consulta (no queda fijo en el modelo cacheado).
    /// Sin tenant devuelve Guid.Empty: el filtro no devuelve filas (falla cerrado).
    /// </summary>
    private Guid TenantActual => currentTenant.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        var entidadesTenant = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Select(t => t.ClrType)
            .ToList();

        foreach (var tipo in entidadesTenant)
        {
            ConfigurarTenantMethod.MakeGenericMethod(tipo).Invoke(this, [modelBuilder]);
        }
    }

    /// <summary>
    /// Para cada entidad del tenant: filtro global por TenantId y FK obligatoria al Comercio.
    /// </summary>
    private void ConfigurarEntidadTenant<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantEntity
    {
        var entidad = modelBuilder.Entity<TEntity>();

        entidad.HasQueryFilter(e => e.TenantId == TenantActual);

        entidad.Property(e => e.TenantId).IsRequired();
        entidad.HasOne<Comercio>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("El registro fue modificado por otro usuario. Volvé a cargarlo e intentá de nuevo.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("Ya existe un registro con esos datos únicos.");
        }
    }
}
