using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddOptions<Features.Suscripciones.MercadoPagoOptions>();
        services.AddScoped<Features.Facturacion.EmisorComprobantesFiscales>();
        services.AddScoped<Common.Auditoria.RegistradorAuditoria>();
        services.AddScoped<Common.Suscripciones.SuscripcionActual>();
        services.TryAddSingleton<Common.Interfaces.IReloj, Common.Interfaces.RelojSistema>(); // los tests lo adelantan

        return services;
    }
}
