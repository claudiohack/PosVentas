using PosVentas.Domain.Common;

namespace PosVentas.Domain.ValueObjects;

public class Sku : ValueObject
{
    public string Value { get; init; }

    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El código SKU es requerido.");

        Value = value.Trim().ToUpper();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}