using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Domain.Common;

namespace PosVentas.WebAPI.Middlewares;

/// <summary>
/// Traduce las excepciones a respuestas RFC 7807 (application/problem+json).
/// Los errores 500 nunca exponen detalles internos al cliente.
/// </summary>
public sealed class GlobalExceptionMiddleware(ILogger<GlobalExceptionMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            await EscribirProblemaAsync(context, ex);
        }
    }

    private async Task EscribirProblemaAsync(HttpContext context, Exception exception)
    {
        var problema = exception switch
        {
            ValidationException ex => Crear(StatusCodes.Status400BadRequest, "Datos inválidos.",
                "Uno o más campos no cumplen las reglas de validación.",
                ex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray())),
            CredencialesInvalidasException ex => Crear(StatusCodes.Status401Unauthorized, "No autenticado.", ex.Message),
            InvitacionInvalidaException ex => Crear(StatusCodes.Status400BadRequest, "Invitación inválida.", ex.Message),
            PlanillaInvalidaException ex => Crear(StatusCodes.Status400BadRequest, "Planilla inválida.", ex.Message),
            AfipRechazoException ex => CrearRechazoAfip(ex),
            AfipNoDisponibleException ex => CrearAfipNoDisponible(ex),
            AccesoDenegadoException ex => Crear(StatusCodes.Status403Forbidden, "Acceso denegado.", ex.Message),
            TenantAccessException => Crear(StatusCodes.Status403Forbidden, "Acceso denegado.",
                "La operación no está permitida para este comercio."),
            NotFoundException ex => Crear(StatusCodes.Status404NotFound, "Recurso no encontrado.", ex.Message),
            ConflictException ex => Crear(StatusCodes.Status409Conflict, "Conflicto.", ex.Message),
            CreditoRechazadoException ex => Crear(StatusCodes.Status400BadRequest, "Crédito rechazado.", ex.Message),
            DomainException ex => Crear(StatusCodes.Status422UnprocessableEntity, "Regla de negocio incumplida.", ex.Message),
            _ => Crear(StatusCodes.Status500InternalServerError, "Error interno del servidor.",
                "Ocurrió un error inesperado. Si persiste, informá el traceId a soporte.")
        };

        var status = problema.Status!.Value;
        if (status >= 500)
            logger.LogError(exception, "Error no controlado en {Metodo} {Ruta}", context.Request.Method, context.Request.Path);
        else if (exception is TenantAccessException)
            logger.LogWarning(exception, "Intento de acceso entre comercios en {Metodo} {Ruta}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Solicitud rechazada ({Status}) en {Metodo} {Ruta}: {Mensaje}",
                status, context.Request.Method, context.Request.Path, exception.Message);

        problema.Instance = context.Request.Path;
        problema.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
    }

    /// <summary>400 con los códigos de ARCA y el id de la venta, que quedó registrada pendiente de facturación.</summary>
    private static ProblemDetails CrearRechazoAfip(AfipRechazoException ex)
    {
        var problema = Crear(StatusCodes.Status400BadRequest, "ARCA rechazó el comprobante.",
            ex.Detalle ?? "La venta quedó registrada como pendiente de facturación. Corregí los datos y reintentá con POST /api/ventas/{id}/facturar.");
        problema.Extensions["ventaId"] = ex.VentaId;
        problema.Extensions["erroresAfip"] = ex.Errores.Select(e => new { codigo = e.Codigo, mensaje = e.Mensaje }).ToArray();
        return problema;
    }

    /// <summary>503: falla de comunicación con ARCA, reintentable.</summary>
    private static ProblemDetails CrearAfipNoDisponible(AfipNoDisponibleException ex)
    {
        var problema = Crear(StatusCodes.Status503ServiceUnavailable, "ARCA no disponible.", ex.Message);
        if (ex.VentaId is { } ventaId)
            problema.Extensions["ventaId"] = ventaId;
        return problema;
    }

    private static ProblemDetails Crear(int status, string titulo, string detalle, IDictionary<string, string[]>? errores = null)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = detalle,
            Type = $"https://httpstatuses.io/{status}"
        };

        if (errores is not null)
            problema.Extensions["errors"] = errores;

        return problema;
    }
}
