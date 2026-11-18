using PosVentas.Domain.Common;

namespace PosVentas.Domain.ValueObjects;

/// <summary>
/// Importe monetario no negativo con su moneda ISO 4217.
/// </summary>
public sealed class Money : ValueObject
{
    public const string MonedaPorDefecto = "ARS";

    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = MonedaPorDefecto;

    private Money(decimal monto, string moneda)
    {
        Monto = monto;
        Moneda = moneda;
    }

    // Requerido por EF Core (tipo complejo)
    private Money() { }

    public static Money Crear(decimal monto, string? moneda = null)
    {
        if (monto < 0)
            throw new DomainException("El monto no puede ser negativo.");

        var codigo = string.IsNullOrWhiteSpace(moneda) ? MonedaPorDefecto : moneda.Trim().ToUpperInvariant();
        if (codigo.Length != 3 || !codigo.All(char.IsAsciiLetterUpper))
            throw new DomainException("La moneda debe ser un código ISO 4217 de 3 letras.");

        return new Money(decimal.Round(monto, 2, MidpointRounding.AwayFromZero), codigo);
    }

    public Money Multiplicar(decimal factor) => Crear(Monto * factor, Moneda);

    public static Money operator +(Money left, Money right)
    {
        if (left.Moneda != right.Moneda)
            throw new DomainException("No se pueden sumar importes de distinta moneda.");
        return Crear(left.Monto + right.Monto, left.Moneda);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Monto;
        yield return Moneda;
    }

    public override string ToString() => $"{Moneda} {Monto:0.00}";
}
