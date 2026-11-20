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

/// <summary>Intento de leer o escribir datos fuera del comercio del usuario autenticado.</summary>
public sealed class TenantAccessException(string message) : Exception(message);
