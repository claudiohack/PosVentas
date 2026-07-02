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
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<CajaSesion> CajaSesiones => Set<CajaSesion>();
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
        modelBuilder.Entity<Proveedor>(entity =>
        {
            entity.ToTable("Proveedores");

            entity.HasKey(p => p.Id);

            entity.Property(p => p.RazonSocial)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(p => p.Cuit)
                .HasMaxLength(20);

            entity.Property(p => p.TipoResponsabilidadIva)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Responsable Inscripto");

            entity.Property(p => p.Telefono)
                .HasMaxLength(50);

            entity.Property(p => p.Email)
                .HasMaxLength(100);

            entity.Property(p => p.Direccion)
                .HasMaxLength(250);

            entity.Property(p => p.NombreContacto)
                .HasMaxLength(100);

            // Índices de base de datos para consultas veloces a futuro
            entity.HasIndex(p => p.RazonSocial);
            entity.HasIndex(p => p.Cuit);
            entity.HasIndex(p => p.EstaActivo);

            // Control de concurrencia optimista nativo (rowversion)
            entity.Property(p => p.Version)
                .IsRowVersion();
        });
        // --- CONFIGURACIÓN DE CAJA ---
        modelBuilder.Entity<Caja>(entity =>
        {
            entity.ToTable("Cajas");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Nombre)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.ComputadoraAsignada)
                .HasMaxLength(100);

            entity.HasIndex(c => c.Nombre);
            entity.HasIndex(c => c.EstaActivo);

            entity.Property(c => c.Version)
                .IsRowVersion();
        });

        // --- CONFIGURACIÓN DE CAJA SESIÓN ---
        modelBuilder.Entity<CajaSesion>(entity =>
        {
            entity.ToTable("CajaSesiones");
            entity.HasKey(s => s.Id);

            // Tipos de precisión para dinero en SQL Server
            entity.Property(s => s.MontoApertura).HasColumnType("decimal(18,2)");
            entity.Property(s => s.MontoCierreSistema).HasColumnType("decimal(18,2)");
            entity.Property(s => s.MontoCierreReal).HasColumnType("decimal(18,2)");
            entity.Property(s => s.DiferenciaArqueo).HasColumnType("decimal(18,2)");

            entity.Property(s => s.UsuarioApertura).IsRequired().HasMaxLength(100);
            entity.Property(s => s.UsuarioCierre).HasMaxLength(100);
            entity.Property(s => s.Observaciones).HasMaxLength(500);

            entity.HasIndex(s => s.EnCurso);

            // Relación 1 a Muchos: Una caja tiene muchas sesiones, una sesión pertenece a una caja
            entity.HasOne(s => s.Caja)
                .WithMany(c => c.Sesiones)
                .HasForeignKey(s => s.CajaId)
                .OnDelete(DeleteBehavior.Restrict); // Evita borrados en cascada accidentales
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}