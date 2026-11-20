using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class ComprobanteFiscalConfiguration : IEntityTypeConfiguration<ComprobanteFiscal>
{
    public void Configure(EntityTypeBuilder<ComprobanteFiscal> builder)
    {
        builder.ToTable("ComprobantesFiscales");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.TipoComprobante).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Resultado).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Cae).HasMaxLength(ComprobanteFiscal.LongitudCae);
        builder.Property(c => c.ObservacionesAfip).HasMaxLength(ComprobanteFiscal.LongitudMaximaObservaciones);
        builder.Property(c => c.CuitEmisor).IsRequired().HasMaxLength(11).IsFixedLength();
        builder.Property(c => c.ImporteTotal).HasPrecision(18, 2);

        builder.HasOne<Venta>().WithMany().HasForeignKey(c => c.VentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ComprobanteFiscal>().WithMany().HasForeignKey(c => c.ComprobanteAsociadoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.VentaId, "IX_ComprobantesFiscales_VentaId");

        // Una venta tiene a lo sumo una factura aprobada (los rechazos quedan como historial)
        builder.HasIndex(c => c.VentaId, "UX_ComprobantesFiscales_VentaId_Aprobado")
            .IsUnique()
            .HasFilter("[Resultado] = 'Aprobado' AND [ComprobanteAsociadoId] IS NULL");

        // Una factura tiene a lo sumo una nota de crédito aprobada (anulación total)
        builder.HasIndex(c => c.ComprobanteAsociadoId, "UX_ComprobantesFiscales_Asociado_Aprobado")
            .IsUnique()
            .HasFilter("[Resultado] = 'Aprobado' AND [ComprobanteAsociadoId] IS NOT NULL");

        // Numeración fiscal única por comercio, tipo y punto de venta
        builder.HasIndex(c => new { c.TenantId, c.TipoComprobante, c.PuntoVenta, c.NumeroComprobante }, "UX_ComprobantesFiscales_Numeracion")
            .IsUnique()
            .HasFilter("[Resultado] = 'Aprobado'");
    }
}

internal sealed class AnulacionVentaConfiguration : IEntityTypeConfiguration<AnulacionVenta>
{
    public void Configure(EntityTypeBuilder<AnulacionVenta> builder)
    {
        builder.ToTable("AnulacionesVenta");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Motivo).IsRequired().HasMaxLength(AnulacionVenta.LongitudMaximaMotivo);
        builder.Property(a => a.MontoTotalRevertido).HasPrecision(18, 2);
        builder.Property(a => a.MontoDevueltoEnCaja).HasPrecision(18, 2);
        builder.Property(a => a.MontoAcreditadoCuentaCorriente).HasPrecision(18, 2);
        builder.Property(a => a.EstadoNotaCredito).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Venta>().WithMany().HasForeignKey(a => a.VentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CajaSesion>().WithMany().HasForeignKey(a => a.CajaSesionId).OnDelete(DeleteBehavior.Restrict);

        // Una venta se anula una sola vez: dos anulaciones simultáneas → la segunda recibe 409
        builder.HasIndex(a => a.VentaId).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.FechaAnulacionUtc });
    }
}
