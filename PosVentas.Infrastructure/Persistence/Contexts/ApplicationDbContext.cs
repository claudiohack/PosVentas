using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Contexts;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.ToTable("Clientes");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.RazonSocial)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(c => c.Documento)
                .HasMaxLength(20);

            entity.Property(c => c.TipoResponsabilidadIva)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Consumidor Final");

            entity.Property(c => c.Telefono)
                .HasMaxLength(50);

            entity.Property(c => c.Email)
                .HasMaxLength(100);

            entity.Property(c => c.Direccion)
                .HasMaxLength(250);

            // Configuración de precisión para montos de dinero (Límite de crédito)
            entity.Property(c => c.LimiteCredito)
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0.00m);

            // Indexamos campos de búsqueda frecuentes para asegurar la velocidad cuando la BD crezca
            entity.HasIndex(c => c.RazonSocial);
            entity.HasIndex(c => c.Documento);
            entity.HasIndex(c => c.EstaActivo);

            // CONTROL DE CONCURRENCIA OPTIMISTA NATIVO:
            // Mapea la propiedad 'Version' como una columna de tipo 'rowversion' / 'timestamp' en SQL Server.
            entity.Property(c => c.Version)
                .IsRowVersion();
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}