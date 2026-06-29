using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace PosVentas.CentralAPI.Middlewares;

public class GlobalExceptionMiddleware(ILogger<GlobalExceptionMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ocurrió un error no controlado en el sistema: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Error de validación en los datos enviados.";
                problemDetails.Detail = "Uno o más campos no cumplen con las reglas de negocio.";
                problemDetails.Extensions["errors"] = validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );
                break;

            case InvalidOperationException invalidOpEx:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Operación inválida para el estado actual.";
                problemDetails.Detail = invalidOpEx.Message;
                break;

            case KeyNotFoundException keyNotFoundEx:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Recurso no encontrado.";
                problemDetails.Detail = keyNotFoundEx.Message;
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Error interno del servidor.";
                problemDetails.Detail = "Ha ocurrido un inconveniente inesperado en la API Central.";
                break;
        }

        var json = JsonSerializer.Serialize(problemDetails);
        await context.Response.WriteAsync(json);
    }
}