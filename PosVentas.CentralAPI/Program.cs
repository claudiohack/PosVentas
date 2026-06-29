using FluentValidation;
using MediatR;
using PosVentas.Application.Common.Behaviors;
using PosVentas.Application.Features.Productos.Commands;
using PosVentas.CentralAPI.Endpoints;
using PosVentas.CentralAPI.Middlewares;
using PosVentas.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Registro de Servicios de Infraestructura Central
builder.Services.AddCentralInfrastructure(builder.Configuration);

// Registro automático de MediatR y el Pipeline de Validación de la capa Application
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(CrearProductoCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});

// Registro de todos los validadores de FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(CrearProductoCommand).Assembly);

// Middleware global contra errores
builder.Services.AddScoped<GlobalExceptionMiddleware>();

//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
// Reemplazá builder.Services.AddEndpointsApiExplorer(); y AddSwaggerGen(); por:
builder.Services.AddOpenApi();

var app = builder.Build();

// 2. HTTP Pipeline
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
    // Reemplazá app.UseSwagger(); y app.UseSwaggerUI(); por:
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi(); // Esto genera el JSON de especificación nativo
    }
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();

// 3. Mapeo de Endpoints
app.MapProductoEndpoints();

app.Run();