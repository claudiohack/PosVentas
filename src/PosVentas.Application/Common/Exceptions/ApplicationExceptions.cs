namespace PosVentas.Application.Common.Exceptions;

/// <summary>El recurso no existe o pertenece a otro comercio (no se distingue a propósito).</summary>
public sealed class NotFoundException(string entidad, object clave)
    : Exception($"No se encontró {entidad} con identificador '{clave}'.");

/// <summary>El estado actual impide la operación (duplicados, concurrencia).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Usuario, contraseña o comercio inválidos. El mensaje nunca indica cuál falló.</summary>
public sealed class CredencialesInvalidasException()
    : Exception("Comercio, usuario o contraseña incorrectos.");

/// <summary>El usuario está autenticado pero su rol no permite la operación.</summary>
public sealed class AccesoDenegadoException(string message) : Exception(message);

/// <summary>
/// ARCA rechazó el comprobante. La venta ya quedó registrada como pendiente de facturación
/// (<see cref="VentaId"/>) y se puede reintentar con POST /api/ventas/{id}/facturar.
/// </summary>
public sealed class AfipRechazoException(Guid ventaId, IReadOnlyList<Interfaces.MensajeAfip> errores)
    : Exception("ARCA rechazó el comprobante: " + string.Join(" | ", errores))
{
    public Guid VentaId { get; } = ventaId;
    public IReadOnlyList<Interfaces.MensajeAfip> Errores { get; } = errores;

    /// <summary>Explicación para el usuario de en qué estado quedó la operación (si no, la de una factura pendiente).</summary>
    public string? Detalle { get; init; }
}

/// <summary>No se pudo comunicar con ARCA (red, timeout o servicio caído). Es reintentable.</summary>
public sealed class AfipNoDisponibleException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Venta que quedó pendiente de facturación, si la hay.</summary>
    public Guid? VentaId { get; init; }
}

/// <summary>Intento de leer o escribir datos fuera del comercio del usuario autenticado.</summary>
public sealed class TenantAccessException(string message) : Exception(message);
