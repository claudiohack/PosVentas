using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class MovimientoCuentaCorrienteConfiguration : IEntityTypeConfiguration<MovimientoCuentaCorriente>
{
    public void Configure(EntityTypeBuilder<MovimientoCuentaCorriente> builder)
    {
        builder.ToTable("MovimientosCuentaCorriente");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Monto).HasPrecision(18, 2);
        builder.Property(m => m.SaldoResultante).HasPrecision(18, 2);
        builder.Property(m => m.Descripcion).IsRequired().HasMaxLength(MovimientoCuentaCorriente.LongitudMaximaDescripcion);

        builder.HasOne<Cliente>().WithMany().HasForeignKey(m => m.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venta>().WithMany().HasForeignKey(m => m.VentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReciboCobranza>().WithMany().HasForeignKey(m => m.ReciboCobranzaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        // Historial de la cuenta: movimientos del cliente por fecha
        builder.HasIndex(m => new { m.ClienteId, m.FechaUtc });
    }
}

internal sealed class ReciboCobranzaConfiguration : IEntityTypeConfiguration<ReciboCobranza>
{
    public void Configure(EntityTypeBuilder<ReciboCobranza> builder)
    {
        builder.ToTable("RecibosCobranza");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Medio).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Monto).HasPrecision(18, 2);
        builder.Property(r => r.SaldoAnterior).HasPrecision(18, 2);
        builder.Property(r => r.SaldoResultante).HasPrecision(18, 2);
        builder.Property(r => r.Observaciones).HasMaxLength(ReciboCobranza.LongitudMaximaObservaciones);

        builder.HasOne<Cliente>().WithMany().HasForeignKey(r => r.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CajaSesion>().WithMany().HasForeignKey(r => r.CajaSesionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.FechaUtc });
    }
}
