using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Infrastructure.Persistence;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta la WebAPI real en memoria contra una base SQL Server exclusiva de esta corrida,
/// creada con las migraciones de EF Core y eliminada al finalizar.
/// Servidor configurable con la variable de entorno POSVENTAS_TEST_SQLSERVER (por defecto .\SQLEXPRESS).
/// </summary>
public sealed class PosVentasApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _nombreBase = $"PosVentasSaaS_Tests_{Guid.NewGuid():N}";

    /// <summary>Clave de firma aleatoria, exclusiva de esta corrida de tests.</summary>
    public string ClaveFirmaJwt { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>Administrador del comercio operador de la plataforma (HS Tech), creado por el inicializador.</summary>
    public const string SlugPlataforma = "hs-tech";
    public const string UsuarioPlataforma = "hs-admin";
    public string PasswordPlataforma { get; } = $"Plataforma-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";

    public string ConnectionString
    {
        get
        {
            var servidor = Environment.GetEnvironmentVariable("POSVENTAS_TEST_SQLSERVER") ?? @".\SQLEXPRESS";
            return $"Server={servidor};Database={_nombreBase};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:PosVentas", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", ClaveFirmaJwt);
        builder.UseSetting("RateLimiting:AuthPorMinuto", "100000");
        builder.UseSetting("Plataforma:Slug", SlugPlataforma);
        builder.UseSetting("Plataforma:AdminUsuario", UsuarioPlataforma);
        builder.UseSetting("Plataforma:AdminPassword", PasswordPlataforma);

        builder.ConfigureTestServices(services =>
        {
            // ARCA simulada: los tests nunca salen a internet ni necesitan un certificado real.
            services.RemoveAll<IAfipService>();
            services.AddSingleton<AfipServiceFalso>();
            services.AddSingleton<IAfipService>(sp => sp.GetRequiredService<AfipServiceFalso>());

            // Claves de Data Protection en memoria: no se escribe nada en el perfil del usuario.
            services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Protección: jamás correr los tests contra la base de desarrollo.
        var conexion = db.Database.GetConnectionString() ?? string.Empty;
        if (!conexion.Contains(_nombreBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Los tests no tomaron la base de pruebas; se aborta para no tocar otra base.");

        await db.Database.MigrateAsync();
        await InicializadorBaseDatos.InicializarAsync(Services, aplicarMigraciones: false);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await DisposeAsync();
    }
}

[CollectionDefinition(Nombre)]
public sealed class ApiCollection : ICollectionFixture<PosVentasApiFactory>
{
    public const string Nombre = "API PosVentas";
}
