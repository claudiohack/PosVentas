using PosVentas.Domain.Common;

namespace PosVentas.Domain.ValueObjects;

/// <summary>
/// Código interno de artículo (SKU / código de barras). Único por comercio.
/// </summary>
public sealed class Sku : ValueObject
{
    public const int LongitudMaxima = 50;

    public string Value { get; }

    private Sku(string value)
    {
        Value = value;
    }

    public static Sku Crear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("El código SKU es obligatorio.");

        var normalizado = value.Trim().ToUpperInvariant();
        if (normalizado.Length > LongitudMaxima)
            throw new DomainException($"El código SKU no puede superar {LongitudMaxima} caracteres.");

        return new Sku(normalizado);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
