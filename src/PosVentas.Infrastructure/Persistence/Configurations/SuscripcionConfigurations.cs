using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Suscripciones;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Planes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Codigo).IsRequired().HasMaxLength(Plan.LongitudMaximaCodigo).IsUnicode(false);
        builder.Property(p => p.Nombre).IsRequired().HasMaxLength(Plan.LongitudMaximaNombre);
        builder.Property(p => p.Descripcion).HasMaxLength(Plan.LongitudMaximaDescripcion);
        builder.Property(p => p.PrecioMensual).HasPrecision(18, 2);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.OwnsMany(p => p.Modulos, modulos =>
        {
            modulos.ToTable("PlanesModulos");
            modulos.WithOwner().HasForeignKey("PlanId");
            modulos.Property(m => m.Codigo).HasMaxLength(ModulosPlan.LongitudMaxima).IsUnicode(false);
            modulos.HasKey("PlanId", nameof(PlanModulo.Codigo));
        });
        builder.Navigation(p => p.Modulos).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_modulos");

        builder.HasIndex(p => p.Codigo).IsUnique();
        // "Un solo predeterminado" lo garantiza ActualizarPlanCommand (sin índice único: el cambio de predeterminado
        // actualiza dos filas en el mismo SaveChanges y SQL Server podría validar el estado intermedio).
    }
}

internal sealed class SuscripcionConfiguration : IEntityTypeConfiguration<Suscripcion>
{
    public void Configure(EntityTypeBuilder<Suscripcion> builder)
    {
        builder.ToTable("Suscripciones");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Ignore(s => s.FinGraciaUtc);

        builder.HasOne<Comercio>().WithOne().HasForeignKey<Suscripcion>(s => s.ComercioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

        // Una suscripción por comercio
        builder.HasIndex(s => s.ComercioId).IsUnique();
    }
}

internal sealed class PagoSuscripcionConfiguration : IEntityTypeConfiguration<PagoSuscripcion>
{
    public void Configure(EntityTypeBuilder<PagoSuscripcion> builder)
    {
        builder.ToTable("PagosSuscripcion");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Monto).HasPrecision(18, 2);
        builder.Property(p => p.Origen).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Referencia).HasMaxLength(PagoSuscripcion.LongitudMaximaReferencia);
        builder.Property(p => p.Nota).HasMaxLength(PagoSuscripcion.LongitudMaximaNota);

        builder.HasOne<Suscripcion>().WithMany().HasForeignKey(p => p.SuscripcionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Comercio>().WithMany().HasForeignKey(p => p.ComercioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.RegistradoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.ComercioId, p.FechaCreacionUtc });
        // Un pago externo (Mercado Pago) se acredita una sola vez aunque el webhook llegue repetido
        builder.HasIndex(p => new { p.Origen, p.Referencia }).IsUnique().HasFilter("[Referencia] IS NOT NULL");
    }
}
