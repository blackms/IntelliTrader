using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a quantity of an asset. Must be non-negative.
/// </summary>
public sealed class Quantity : ValueObject, IComparable<Quantity>
{
    /// <summary>
    /// The quantity value.
    /// </summary>
    public decimal Value { get; }

    private Quantity(decimal value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a Quantity instance.
    /// </summary>
    public static Quantity Create(decimal value)
    {
        if (value < 0)
            throw new ArgumentException("Quantity cannot be negative", nameof(value));

        return new Quantity(value);
    }

    /// <summary>
    /// Creates a zero quantity.
    /// </summary>
    public static Quantity Zero => new(0m);

    /// <summary>
    /// Checks if the quantity is zero.
    /// </summary>
    public bool IsZero => Value == 0m;

    /// <summary>
    /// Rounds the quantity to the specified decimal places.
    /// </summary>
    public Quantity Round(int decimals) => new(Math.Round(Value, decimals, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Truncates the quantity to the specified decimal places (rounds down).
    /// </summary>
    public Quantity Truncate(int decimals)
    {
        var multiplier = (decimal)Math.Pow(10, decimals);
        return new Quantity(Math.Truncate(Value * multiplier) / multiplier);
    }

    public static Quantity operator +(Quantity left, Quantity right) => new(left.Value + right.Value);
    public static Quantity operator -(Quantity left, Quantity right)
    {
        var result = left.Value - right.Value;
        if (result < 0)
            throw new InvalidOperationException("Quantity subtraction would result in negative value");
        return new Quantity(result);
    }

    public static Quantity operator *(Quantity quantity, decimal multiplier)
    {
        if (multiplier < 0)
            throw new ArgumentException("Cannot multiply quantity by negative value", nameof(multiplier));
        return new Quantity(quantity.Value * multiplier);
    }

    public static Quantity operator /(Quantity quantity, decimal divisor)
    {
        if (divisor <= 0)
            throw new ArgumentException("Cannot divide quantity by zero or negative value", nameof(divisor));
        return new Quantity(quantity.Value / divisor);
    }

    public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;
    public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;
    public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;
    public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;

    public int CompareTo(Quantity? other)
    {
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString("G");

    public static implicit operator decimal(Quantity quantity) => quantity.Value;
}
