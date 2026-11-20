using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class ProveedorConfiguration : IEntityTypeConfiguration<Proveedor>
{
    public void Configure(EntityTypeBuilder<Proveedor> builder)
    {
        builder.ToTable("Proveedores");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.RazonSocial).IsRequired().HasMaxLength(Proveedor.LongitudMaximaRazonSocial);
        builder.Property(p => p.Cuit).IsRequired().HasMaxLength(Proveedor.LongitudCuit).IsFixedLength();
        builder.Property(p => p.Telefono).HasMaxLength(Proveedor.LongitudMaximaTelefono);
        builder.Property(p => p.Email).HasMaxLength(Proveedor.LongitudMaximaEmail);
        builder.Property(p => p.Direccion).HasMaxLength(Proveedor.LongitudMaximaDireccion);

        // Un proveedor por CUIT dentro de cada comercio (dos comercios pueden tener el mismo proveedor)
        builder.HasIndex(p => new { p.TenantId, p.Cuit }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.RazonSocial });
    }
}

internal sealed class CompraConfiguration : IEntityTypeConfiguration<Compra>
{
    public void Configure(EntityTypeBuilder<Compra> builder)
    {
        builder.ToTable("Compras");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.NumeroComprobante).IsRequired().HasMaxLength(Compra.LongitudMaximaComprobante);
        builder.Property(c => c.FormaPago).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.MontoTotal).HasPrecision(18, 2);
        builder.Property(c => c.Observaciones).HasMaxLength(Compra.LongitudMaximaObservaciones);

        builder.HasOne<Proveedor>().WithMany().HasForeignKey(c => c.ProveedorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CajaSesion>().WithMany().HasForeignKey(c => c.CajaSesionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Detalles)
            .WithOne()
            .HasForeignKey(d => d.CompraId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Detalles).UsePropertyAccessMode(PropertyAccessMode.Field);

        // El mismo comprobante de un proveedor no se carga dos veces (evita duplicar stock)
        builder.HasIndex(c => new { c.TenantId, c.ProveedorId, c.NumeroComprobante }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.FechaCompra });
    }
}

internal sealed class CompraDetalleConfiguration : IEntityTypeConfiguration<CompraDetalle>
{
    public void Configure(EntityTypeBuilder<CompraDetalle> builder)
    {
        builder.ToTable("CompraDetalles");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.NombreProducto).IsRequired().HasMaxLength(Producto.LongitudMaximaNombre);
        builder.Property(d => d.Cantidad).HasPrecision(18, 3);
        builder.Property(d => d.CostoUnitario).HasPrecision(18, 2);
        builder.Property(d => d.Subtotal).HasPrecision(18, 2);

        builder.HasOne<Producto>().WithMany().HasForeignKey(d => d.ProductoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.ProductoId);
    }
}
