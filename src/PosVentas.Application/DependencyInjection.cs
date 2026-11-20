using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Application.Common.Behaviors;

namespace PosVentas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(AutorizacionBehavior<,>)); // primero: no validar datos de quien no tiene permiso
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddOptions<Features.Facturacion.FacturacionOptions>();
        services.AddScoped<Features.Facturacion.EmisorComprobantesFiscales>();

        return services;
    }
}
