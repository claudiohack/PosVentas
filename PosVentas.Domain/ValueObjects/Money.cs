using PosVentas.Domain.Common;

namespace PosVentas.Domain.ValueObjects;

public class Money : ValueObject
{
    public decimal Monto { get; init; }
    public string Moneda { get; init; } = "ARS";

    public Money(decimal monto, string moneda = "ARS")
    {
        if (monto < 0)
            throw new ArgumentException("El monto de dinero no puede ser negativo.");

        Monto = monto;
        Moneda = string.IsNullOrWhiteSpace(moneda) ? "ARS" : moneda.ToUpper();
    }

#pragma warning disable CS8618
    protected Money() { }
#pragma warning restore CS8618

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Monto;
        yield return Moneda;
    }

    public static Money operator +(Money left, Money right)
    {
        if (left.Moneda != right.Moneda)
            throw new InvalidOperationException("No se pueden sumar diferentes tipos de moneda.");
        return new Money(left.Monto + right.Monto, left.Moneda);
    }

    public static Money operator *(Money money, decimal multiplicador) =>
        new(money.Monto * multiplicador, money.Moneda);
}