using MediatR;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Features.Productos.Commands;

namespace PosVentas.CentralAPI.Endpoints;

public static class ProductoEndpoints
{
    public static IEndpointRouteBuilder MapProductoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/productos")
            .WithTags("Productos (Catálogo Maestro Central)");

        group.MapPost("/", async (
            [FromBody] CrearProductoCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var productoId = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/productos/{productoId}", new { Id = productoId });
        })
        .WithName("CrearProducto");
        //.WithOpenApi();

        return app;
    }
}