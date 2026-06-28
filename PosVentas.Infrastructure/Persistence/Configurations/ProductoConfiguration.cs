using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Infrastructure.Persistence.Configurations;

public class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.ToTable("Productos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Nombre).IsRequired().HasMaxLength(150);

        // Mapeo del Value Object Sku
        builder.Property(p => p.Sku)
            .HasConversion(sku => sku.Value, value => new Sku(value))
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Sku).IsUnique();

        // Mapeo aplanado del Value Object Money
        builder.OwnsOne(p => p.Precio, money =>
        {
            money.Property(m => m.Monto).HasColumnName("PrecioMonto").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Moneda).HasColumnName("PrecioMoneda").HasMaxLength(3).IsRequired();
        });

        builder.Property(p => p.PorcentajeIva).HasPrecision(5, 2).IsRequired();
        builder.Property(p => p.StockActual).IsRequired();
        builder.Property(p => p.VenderSinStock).IsRequired();
        builder.Property(p => p.FechaCreacion).IsRequired();

        builder.HasOne(p => p.Categoria)
            .WithMany(c => c.Productos)
            .HasForeignKey(p => p.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}