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

        // Configuración fiscal (ARCA)
        builder.Property(c => c.CondicionIva).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.DomicilioFiscal).HasMaxLength(Comercio.LongitudMaximaDomicilio);
        builder.Property(c => c.IngresosBrutos).HasMaxLength(Comercio.LongitudMaximaIngresosBrutos);
        builder.Property(c => c.CertificadoAfipProtegido); // nvarchar(max), cifrado con Data Protection
        builder.Property(c => c.CertificadoAfipSujeto).HasMaxLength(500);
        builder.Property(c => c.TicketAccesoAfipProtegido);
        builder.Ignore(c => c.FacturacionConfigurada);

        builder.HasIndex(c => c.Slug).IsUnique();

        // Un único comercio operador de la plataforma
        builder.HasIndex(c => c.EsPlataforma).IsUnique().HasFilter("[EsPlataforma] = 1")
            .HasDatabaseName("UX_Comercios_Plataforma");
    }
}

internal sealed class InvitacionComercioConfiguration : IEntityTypeConfiguration<InvitacionComercio>
{
    public void Configure(EntityTypeBuilder<InvitacionComercio> builder)
    {
        builder.ToTable("InvitacionesComercio");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TokenHash).IsRequired().HasMaxLength(InvitacionComercio.LongitudHash).IsFixedLength();
        builder.Property(i => i.EmailDestinatario).HasMaxLength(InvitacionComercio.LongitudMaximaEmail);
        builder.Property(i => i.Nota).HasMaxLength(InvitacionComercio.LongitudMaximaNota);
        builder.Property(i => i.RowVersion).IsRowVersion();
        builder.Ignore(i => i.EstaVigente);

        builder.HasOne<Usuario>().WithMany().HasForeignKey(i => i.CreadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Comercio>().WithMany().HasForeignKey(i => i.ComercioCreadoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.TokenHash).IsUnique();
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
        builder.Property(u => u.Email).HasMaxLength(Usuario.LongitudMaximaEmail);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(100);
        builder.Property(u => u.SecurityStamp).IsRequired();

        // Un rol con usuarios no se borra (ni físico ni lógico: lo valida el caso de uso)
        builder.HasOne(u => u.Rol).WithMany().HasForeignKey(u => u.RolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.RolId);

        // El nombre de usuario es único dentro de cada comercio
        builder.HasIndex(u => new { u.TenantId, u.Username }).IsUnique();
    }
}

internal sealed class RolConfiguration : IEntityTypeConfiguration<Rol>
{
    public void Configure(EntityTypeBuilder<Rol> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Nombre).IsRequired().HasMaxLength(Rol.LongitudMaximaNombre);
        builder.Property(r => r.Descripcion).HasMaxLength(Rol.LongitudMaximaDescripcion);
        builder.Property(r => r.CodigoSistema).HasMaxLength(Rol.LongitudMaximaCodigo).IsUnicode(false);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Ignore(r => r.EsSistema);
        builder.Ignore(r => r.EsAdministrador);

        // Permisos: tabla propia (RolId + Codigo), siempre cargada con el rol
        builder.OwnsMany(r => r.Permisos, permisos =>
        {
            permisos.ToTable("RolesPermisos");
            permisos.WithOwner().HasForeignKey("RolId");
            permisos.Property(p => p.Codigo).HasMaxLength(Domain.Seguridad.Permisos.LongitudMaxima).IsUnicode(false);
            permisos.HasKey("RolId", nameof(RolPermiso.Codigo));
        });
        builder.Navigation(r => r.Permisos).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_permisos");

        // Nombre único entre los roles activos de cada comercio (uno eliminado libera el nombre)
        builder.HasIndex(r => new { r.TenantId, r.Nombre }).IsUnique().HasFilter("[EstaActivo] = 1")
            .HasDatabaseName("UX_Roles_Tenant_Nombre_Activo");
        // Un solo rol de sistema de cada tipo por comercio
        builder.HasIndex(r => new { r.TenantId, r.CodigoSistema }).IsUnique().HasFilter("[CodigoSistema] IS NOT NULL")
            .HasDatabaseName("UX_Roles_Tenant_CodigoSistema");
    }
}

internal sealed class RegistroAuditoriaConfiguration : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> builder)
    {
        builder.ToTable("RegistrosAuditoria");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Accion).IsRequired().HasMaxLength(RegistroAuditoria.LongitudMaximaAccion).IsUnicode(false);
        builder.Property(r => r.Entidad).IsRequired().HasMaxLength(RegistroAuditoria.LongitudMaximaEntidad).IsUnicode(false);
        builder.Property(r => r.NombreUsuario).HasMaxLength(RegistroAuditoria.LongitudMaximaUsuario);
        builder.Property(r => r.Detalle).HasMaxLength(RegistroAuditoria.LongitudMaximaDetalle);

        // Sin FK a Usuarios a propósito: el registro sobrevive aunque cambie o se depure el usuario.
        builder.HasIndex(r => new { r.TenantId, r.FechaCreacionUtc });
        builder.HasIndex(r => new { r.TenantId, r.Entidad, r.EntidadId });
    }
}
