namespace PosVentas.Domain.Common;

/// <summary>
/// Violación de una regla de negocio del dominio.
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>
/// La venta a cuenta corriente no es admisible para el cliente: no tiene la cuenta habilitada
/// o el cargo supera su límite de crédito. La API la informa como 400 Bad Request.
/// </summary>
public sealed class CreditoRechazadoException(string message) : DomainException(message);
