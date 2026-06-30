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

        // 1. Mapeo aplanado del NUEVO Value Object Money para COMPRA/COSTO
        builder.OwnsOne(p => p.PrecioCompra, money =>
        {
            money.Property(m => m.Monto).HasColumnName("PrecioCompraMonto").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Moneda).HasColumnName("PrecioCompraMoneda").HasMaxLength(3).IsRequired();
        });

        // 2. Mapeo del campo PorcentajeGanancia (simple decimal)
        builder.Property(p => p.PorcentajeGanancia).HasPrecision(5, 2).IsRequired();

        // 3. Mapeo aplanado del Value Object Money para VENTA (El original)
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