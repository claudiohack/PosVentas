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

        builder.HasIndex(c => c.VentaId, "IX_ComprobantesFiscales_VentaId");

        // Una venta tiene a lo sumo un comprobante aprobado (los rechazos quedan como historial)
        builder.HasIndex(c => c.VentaId, "UX_ComprobantesFiscales_VentaId_Aprobado")
            .IsUnique()
            .HasFilter("[Resultado] = 'Aprobado'");

        // Numeración fiscal única por comercio, tipo y punto de venta
        builder.HasIndex(c => new { c.TenantId, c.TipoComprobante, c.PuntoVenta, c.NumeroComprobante }, "UX_ComprobantesFiscales_Numeracion")
            .IsUnique()
            .HasFilter("[Resultado] = 'Aprobado'");
    }
}
