namespace PosVentas.Application.Common.Interfaces;

/// <summary>
/// Hora actual para la lógica de suscripciones (prueba, vencimiento, gracia). Es propia y no el TimeProvider
/// global a propósito: los tests la adelantan sin afectar la validación de tokens ni el rate limiting.
/// </summary>
public interface IReloj
{
    DateTime AhoraUtc { get; }
}

public sealed class RelojSistema : IReloj
{
    public DateTime AhoraUtc => DateTime.UtcNow;
}
