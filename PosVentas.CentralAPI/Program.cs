using Microsoft.EntityFrameworkCore;
using PosVentas.Application;
using PosVentas.CentralAPI;
using PosVentas.CentralAPI.Endpoints;
using PosVentas.Domain.Repositories;                     // Donde viven tus interfaces (IProductoRepository, IUnitOfWork)
using PosVentas.Infrastructure.Persistence.Contexts;     // Tu carpeta real del DbContext
using PosVentas.Infrastructure.Persistence.Repositories; // Tu carpeta real de los Repositorios físicos

var builder = WebApplication.CreateBuilder(args);

// 1. CONFIGURACIÓN DE OPENAPI (Nativo de .NET 10)
builder.Services.AddOpenApi();

// 2. INYECCIÓN DE LA CAPA DE INFRAESTRUCTURA (Base de Datos SQL Express)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CentralConnection"),
        b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

// 3. REGISTRO DE REPOSITORIOS Y UTILITIES (Inyección de Dependencias)
builder.Services.AddScoped<IProductoRepository, ProductoRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Nota: Si en el futuro creás Categoría, solo tenés que descomentar la línea de abajo
// builder.Services.AddScoped<ICategoriaRepository, CategoriaRepository>();

// 4. INYECCIÓN DE LA CAPA DE APPLICATION (MediatR, FluentValidation, etc.)
builder.Services.AddApplicationServices();

var app = builder.Build();

// 5. CONFIGURACIÓN DEL PIPELINE HTTP (Middlewares)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Genera el JSON de OpenAPI en /openapi/v1.json
}

app.UseHttpsRedirection();

// 6. REGISTRO DE ENDPOINTS (Minimal APIs para Productos)
app.MapProductoEndpoints();

app.Run();