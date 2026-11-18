using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class CategoriaConfiguration : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> builder)
    {
        builder.ToTable("Categorias");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Nombre).IsRequired().HasMaxLength(Categoria.LongitudMaximaNombre);
        builder.Property(c => c.Descripcion).HasMaxLength(Categoria.LongitudMaximaDescripcion);

        builder.HasIndex(c => new { c.TenantId, c.Nombre }).IsUnique();
    }
}

internal sealed class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.ToTable("Productos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Sku)
            .HasConversion(sku => sku.Value, valor => Sku.Crear(valor))
            .IsRequired()
            .HasMaxLength(Sku.LongitudMaxima);

        builder.Property(p => p.Nombre).IsRequired().HasMaxLength(Producto.LongitudMaximaNombre);

        builder.ComplexProperty(p => p.PrecioCompra, money =>
        {
            money.Property(m => m.Monto).HasColumnName("PrecioCompra").HasPrecision(18, 2);
            money.Property(m => m.Moneda).HasColumnName("PrecioCompraMoneda").HasMaxLength(3).IsFixedLength();
        });

        builder.ComplexProperty(p => p.PrecioVenta, money =>
        {
            money.Property(m => m.Monto).HasColumnName("PrecioVenta").HasPrecision(18, 2);
            money.Property(m => m.Moneda).HasColumnName("PrecioVentaMoneda").HasMaxLength(3).IsFixedLength();
        });

        builder.Property(p => p.PorcentajeGanancia).HasPrecision(9, 2);
        builder.Property(p => p.PorcentajeIva).HasPrecision(5, 2);
        builder.Property(p => p.StockActual).HasPrecision(18, 3);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne(p => p.Categoria)
            .WithMany()
            .HasForeignKey(p => p.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        // SKU único por comercio (dos comercios pueden usar el mismo código)
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Nombre });
    }
}

internal sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("Clientes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.RazonSocial).IsRequired().HasMaxLength(Cliente.LongitudMaximaRazonSocial);
        builder.Property(c => c.Documento).HasMaxLength(Cliente.LongitudMaximaDocumento);
        builder.Property(c => c.CondicionIva).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Telefono).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(150);
        builder.Property(c => c.Direccion).HasMaxLength(200);
        builder.Property(c => c.LimiteCredito).HasPrecision(18, 2);

        builder.HasIndex(c => new { c.TenantId, c.Documento })
            .IsUnique()
            .HasFilter("[Documento] IS NOT NULL");
    }
}
