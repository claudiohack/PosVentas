using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PosVentas.CentralAPI.Endpoints; // O el namespace que tenga tu archivo

public static class ProductoEndpoints
{
    public static IEndpointRouteBuilder MapProductoEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Esto crea el grupo base: /api/productos
        var group = app.MapGroup("/api/productos");

        // 2. Esto añade el sub-path: /ping (Ruta final: /api/productos/ping)
        group.MapGet("/ping", () => Results.Ok(new { message = "API de Productos funcionando!" }));

        // Al final retornamos el app
        return app;
    }
}