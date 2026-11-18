using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Persistence.Configurations;

internal sealed class ComercioConfiguration : IEntityTypeConfiguration<Comercio>
{
    public void Configure(EntityTypeBuilder<Comercio> builder)
    {
        builder.ToTable("Comercios");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Nombre).IsRequired().HasMaxLength(Comercio.LongitudMaximaNombre);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(Comercio.LongitudMaximaSlug);
        builder.Property(c => c.Cuit).HasMaxLength(13);

        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuarios");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.NombreCompleto).IsRequired().HasMaxLength(Usuario.LongitudMaximaNombre);
        builder.Property(u => u.Username).IsRequired().HasMaxLength(Usuario.LongitudMaximaUsername);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Rol).HasConversion<string>().HasMaxLength(20);

        // El nombre de usuario es único dentro de cada comercio
        builder.HasIndex(u => new { u.TenantId, u.Username }).IsUnique();
    }
}
