using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class CajaConfiguration : IEntityTypeConfiguration<Caja>
{
    public void Configure(EntityTypeBuilder<Caja> builder)
    {
        builder.ToTable("Cajas");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Nombre).IsRequired().HasMaxLength(Caja.LongitudMaximaNombre);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => new { c.TenantId, c.Nombre }).IsUnique();
    }
}

internal sealed class CajaSesionConfiguration : IEntityTypeConfiguration<CajaSesion>
{
    public void Configure(EntityTypeBuilder<CajaSesion> builder)
    {
        builder.ToTable("CajaSesiones");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.MontoApertura).HasPrecision(18, 2);
        builder.Property(s => s.MontoCierreSistema).HasPrecision(18, 2);
        builder.Property(s => s.MontoCierreReal).HasPrecision(18, 2);
        builder.Property(s => s.DiferenciaArqueo).HasPrecision(18, 2);
        builder.Property(s => s.Observaciones).HasMaxLength(500);
        builder.Property(s => s.TotalVendido).HasPrecision(18, 2);
        builder.Property(s => s.TotalCobranzas).HasPrecision(18, 2);
        builder.Property(s => s.TotalPagosProveedores).HasPrecision(18, 2);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasOne(s => s.Caja)
            .WithMany()
            .HasForeignKey(s => s.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(s => s.UsuarioAperturaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(s => s.UsuarioCierreId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una caja solo puede tener un turno abierto a la vez (garantizado por la base)
        builder.HasIndex(s => s.CajaId)
            .IsUnique()
            .HasFilter("[EnCurso] = 1")
            .HasDatabaseName("UX_CajaSesiones_CajaId_EnCurso");
    }
}

internal sealed class VentaConfiguration : IEntityTypeConfiguration<Venta>
{
    public void Configure(EntityTypeBuilder<Venta> builder)
    {
        builder.ToTable("Ventas");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Ignore(v => v.EsFiscal);

        builder.Property(v => v.TotalNetoNoGravado).HasPrecision(18, 2);
        builder.Property(v => v.TotalNetoGravado).HasPrecision(18, 2);
        builder.Property(v => v.TotalIva).HasPrecision(18, 2);
        builder.Property(v => v.TotalGeneral).HasPrecision(18, 2);
        builder.Property(v => v.TipoComprobante).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Cae).HasMaxLength(Venta.LongitudMaximaCae);

        // Claves foráneas explícitas
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(v => v.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(v => v.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CajaSesion>()
            .WithMany()
            .HasForeignKey(v => v.CajaSesionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Detalles)
            .WithOne()
            .HasForeignKey(d => d.VentaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(v => v.Detalles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(v => v.Pagos)
            .WithOne()
            .HasForeignKey(p => p.VentaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(v => v.Pagos).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(v => v.TotalPagado);
        builder.Ignore(v => v.MontoCuentaCorriente);
        builder.Property(v => v.Vuelto).HasPrecision(18, 2);

        builder.HasIndex(v => new { v.TenantId, v.FechaUtc });

        // Numeración fiscal única por comercio, tipo y punto de venta
        builder.HasIndex(v => new { v.TenantId, v.TipoComprobante, v.PuntoVenta, v.NumeroComprobante })
            .IsUnique()
            .HasFilter("[NumeroComprobante] IS NOT NULL");
    }
}

internal sealed class VentaPagoConfiguration : IEntityTypeConfiguration<VentaPago>
{
    public void Configure(EntityTypeBuilder<VentaPago> builder)
    {
        builder.ToTable("VentaPagos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Medio).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Monto).HasPrecision(18, 2);
    }
}

internal sealed class MovimientoCajaConfiguration : IEntityTypeConfiguration<MovimientoCaja>
{
    public void Configure(EntityTypeBuilder<MovimientoCaja> builder)
    {
        builder.ToTable("MovimientosCaja");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Medio).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Monto).HasPrecision(18, 2);
        builder.Property(m => m.Descripcion).IsRequired().HasMaxLength(MovimientoCaja.LongitudMaximaDescripcion);

        builder.HasOne<CajaSesion>()
            .WithMany()
            .HasForeignKey(m => m.CajaSesionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Venta>()
            .WithMany()
            .HasForeignKey(m => m.VentaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Compra>()
            .WithMany()
            .HasForeignKey(m => m.CompraId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(m => m.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Consulta del arqueo: movimientos del turno por medio de pago
        builder.HasIndex(m => new { m.CajaSesionId, m.Medio });
    }
}

internal sealed class VentaDetalleConfiguration : IEntityTypeConfiguration<VentaDetalle>
{
    public void Configure(EntityTypeBuilder<VentaDetalle> builder)
    {
        builder.ToTable("VentaDetalles");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Ignore(d => d.TotalConIva);

        builder.Property(d => d.NombreProducto).IsRequired().HasMaxLength(Producto.LongitudMaximaNombre);
        builder.Property(d => d.Cantidad).HasPrecision(18, 3);
        builder.Property(d => d.PrecioUnitario).HasPrecision(18, 2);
        builder.Property(d => d.PorcentajeIva).HasPrecision(5, 2);
        builder.Property(d => d.Subtotal).HasPrecision(18, 2);
        builder.Property(d => d.MontoIva).HasPrecision(18, 2);

        builder.HasOne<Producto>()
            .WithMany()
            .HasForeignKey(d => d.ProductoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
