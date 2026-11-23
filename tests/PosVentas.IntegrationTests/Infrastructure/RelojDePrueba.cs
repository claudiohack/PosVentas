using PosVentas.Application.Common.Interfaces;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>
/// Reloj de la lógica de suscripciones: permite adelantar el tiempo para probar vencimientos sin esperar.
/// Los tests de la colección corren en serie; quien lo adelanta lo restablece en un finally.
/// </summary>
public sealed class RelojDePrueba : IReloj
{
    private TimeSpan _desfase = TimeSpan.Zero;

    public DateTime AhoraUtc => DateTime.UtcNow + _desfase;

    public void Adelantar(TimeSpan tiempo) => _desfase += tiempo;

    public void Restablecer() => _desfase = TimeSpan.Zero;
}
