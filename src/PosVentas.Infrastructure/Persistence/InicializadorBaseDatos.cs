using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Infrastructure.Persistence;

/// <summary>Comercio operador del SaaS y su primer administrador, leídos de configuración (sección "Plataforma").</summary>
public sealed class PlataformaOptions
{
    public const string SectionName = "Plataforma";

    public string Nombre { get; set; } = "HS Tech";
    public string Slug { get; set; } = "hs-tech";
    public string AdminUsuario { get; set; } = string.Empty;
    public string AdminNombre { get; set; } = "Administrador HS Tech";

    /// <summary>Solo para crear el primer administrador: va por User Secrets o variable de entorno, nunca en el repositorio.</summary>
    public string AdminPassword { get; set; } = string.Empty;
}

/// <summary>
/// Prepara la base al arrancar (contenedores): aplica las migraciones pendientes y, si todavía no existe,
/// crea el comercio operador de la plataforma con su administrador. Es idempotente.
/// </summary>
public static class InicializadorBaseDatos
{
    public static async Task InicializarAsync(IServiceProvider services, bool aplicarMigraciones, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(InicializadorBaseDatos));

        if (aplicarMigraciones)
        {
            var pendientes = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pendientes.Count > 0)
            {
                logger.LogInformation("Aplicando {Cantidad} migraciones: {Migraciones}", pendientes.Count, string.Join(", ", pendientes));
                await db.Database.MigrateAsync(ct);
            }
        }

        await CrearPlataformaSiFaltaAsync(provider, db, logger, ct);
    }

    private static async Task CrearPlataformaSiFaltaAsync(IServiceProvider provider, ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Comercios.AnyAsync(c => c.EsPlataforma, ct))
            return;

        var opciones = provider.GetRequiredService<IConfiguration>().GetSection(PlataformaOptions.SectionName).Get<PlataformaOptions>()
                       ?? new PlataformaOptions();
        if (string.IsNullOrWhiteSpace(opciones.AdminUsuario) || string.IsNullOrWhiteSpace(opciones.AdminPassword))
        {
            logger.LogWarning("No existe el comercio operador de la plataforma y falta Plataforma:AdminUsuario / Plataforma:AdminPassword: " +
                              "no se podrán emitir invitaciones hasta configurarlos.");
            return;
        }
        if (opciones.AdminPassword.Length < 12 || !opciones.AdminPassword.Any(char.IsLetter) || !opciones.AdminPassword.Any(char.IsDigit))
            throw new InvalidOperationException("Plataforma:AdminPassword debe tener al menos 12 caracteres, con letras y números.");

        var comercio = Comercio.CrearPlataforma(opciones.Nombre, opciones.Slug);
        db.Comercios.Add(comercio);
        provider.GetRequiredService<ICurrentTenantService>().EstablecerTenant(comercio.Id);

        var hasher = provider.GetRequiredService<IPasswordHasher>();
        db.Usuarios.Add(Usuario.Crear(opciones.AdminNombre, opciones.AdminUsuario, hasher.Hash(opciones.AdminPassword), RolUsuario.Administrador));
        db.Clientes.Add(Cliente.CrearConsumidorFinalGenerico());
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Comercio operador de la plataforma '{Slug}' creado con el administrador '{Usuario}'.", comercio.Slug, opciones.AdminUsuario);
    }
}
