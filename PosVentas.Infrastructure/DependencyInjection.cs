using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosVentas.Domain.Repositories;
using PosVentas.Infrastructure.Persistence.Contexts;
using PosVentas.Infrastructure.Persistence.Repositories;

namespace PosVentas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCentralInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("CentralConnection")));

        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped(typeof(IBaseRepository<,>), typeof(BaseRepository<,>));
        services.AddScoped<IProductoRepository, ProductoRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}