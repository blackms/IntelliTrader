using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a price value. Must be non-negative.
/// </summary>
public sealed class Price : ValueObject, IComparable<Price>
{
    /// <summary>
    /// The price value.
    /// </summary>
    public decimal Value { get; }

    private Price(decimal value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a Price instance.
    /// </summary>
    public static Price Create(decimal value)
    {
        if (value < 0)
            throw new ArgumentException("Price cannot be negative", nameof(value));

        return new Price(value);
    }

    /// <summary>
    /// Creates a zero price.
    /// </summary>
    public static Price Zero => new(0m);

    /// <summary>
    /// Checks if the price is zero.
    /// </summary>
    public bool IsZero => Value == 0m;

    /// <summary>
    /// Calculates the percentage change from another price.
    /// </summary>
    /// <param name="fromPrice">The original price to compare against</param>
    /// <returns>The percentage change (e.g., 10 for 10% increase)</returns>
    public decimal PercentageChangeFrom(Price fromPrice)
    {
        if (fromPrice.IsZero)
            throw new InvalidOperationException("Cannot calculate percentage change from zero price");

        return ((Value - fromPrice.Value) / fromPrice.Value) * 100m;
    }

    /// <summary>
    /// Applies a percentage change to this price.
    /// </summary>
    /// <param name="percentageChange">The percentage change to apply (e.g., 10 for 10% increase)</param>
    public Price ApplyPercentageChange(decimal percentageChange)
    {
        var newValue = Value * (1 + percentageChange / 100m);
        return new Price(Math.Max(0, newValue));
    }

    /// <summary>
    /// Rounds the price to the specified decimal places.
    /// </summary>
    public Price Round(int decimals) => new(Math.Round(Value, decimals, MidpointRounding.AwayFromZero));

    public static bool operator >(Price left, Price right) => left.Value > right.Value;
    public static bool operator <(Price left, Price right) => left.Value < right.Value;
    public static bool operator >=(Price left, Price right) => left.Value >= right.Value;
    public static bool operator <=(Price left, Price right) => left.Value <= right.Value;

    public int CompareTo(Price? other)
    {
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString("G");

    public static implicit operator decimal(Price price) => price.Value;
}
