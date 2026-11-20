using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Infrastructure.Persistence;
using PosVentas.Infrastructure.Persistence.Interceptors;
using PosVentas.Infrastructure.Security;

namespace PosVentas.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PosVentas";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Falta la cadena de conexión '{ConnectionStringName}'.");

        // --- Persistencia ---
        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            });
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // --- Seguridad ---
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer y Jwt:Audience son obligatorios.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey ?? string.Empty) >= JwtOptions.LongitudMinimaClave,
                $"Jwt:SigningKey debe tener al menos {JwtOptions.LongitudMinimaClave} bytes. Configurala con User Secrets o la variable de entorno Jwt__SigningKey.")
            .Validate(o => o.ExpiracionMinutos is > 0 and <= 1440,
                "Jwt:ExpiracionMinutos debe estar entre 1 y 1440.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // --- Secretos en reposo (certificado ARCA y ticket de acceso) ---
        // En producción el anillo de claves debe vivir fuera del servidor web y de la base
        // (DataProtection:DirectorioClaves en un volumen protegido, o Azure Key Vault / Blob).
        var dataProtection = services.AddDataProtection().SetApplicationName("PosVentas");
        var directorioClaves = configuration["DataProtection:DirectorioClaves"];
        if (!string.IsNullOrWhiteSpace(directorioClaves))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));

        // --- Facturación electrónica ARCA (WSAA + WSFEv1) ---
        services.AddOptions<Afip.AfipOptions>().Bind(configuration.GetSection(Afip.AfipOptions.SectionName));
        services.AddHttpClient<Afip.AfipSoapCliente>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Afip.AfipOptions>>().Value.TimeoutSegundos));
        services.AddSingleton<Afip.CertificadoAfipProtector>();
        services.AddSingleton<ICertificadoAfipProtector>(sp => sp.GetRequiredService<Afip.CertificadoAfipProtector>());
        services.AddScoped<IAfipService, Afip.AfipService>();
        services.AddSingleton<IGeneradorQr, Afip.GeneradorQr>();

        return services;
    }
}
