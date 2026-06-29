using Microsoft.Extensions.DependencyInjection;

namespace PosVentas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Registra MediatR buscando los Handlers en este mismo proyecto (Application)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        // Aquí podés agregar en el futuro FluentValidation, AutoMapper, etc.
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}