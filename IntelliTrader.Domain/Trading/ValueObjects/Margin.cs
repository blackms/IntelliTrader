using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a profit/loss margin as a percentage.
/// Positive values indicate profit, negative values indicate loss.
/// </summary>
public sealed class Margin : ValueObject, IComparable<Margin>
{
    /// <summary>
    /// The margin percentage value.
    /// </summary>
    public decimal Percentage { get; }

    private Margin(decimal percentage)
    {
        Percentage = percentage;
    }

    /// <summary>
    /// Creates a Margin instance from a percentage value.
    /// </summary>
    /// <param name="percentage">The margin percentage (e.g., 5.5 for 5.5%)</param>
    public static Margin FromPercentage(decimal percentage)
    {
        return new Margin(percentage);
    }

    /// <summary>
    /// Calculates margin from cost and current value.
    /// </summary>
    /// <param name="cost">The original cost/investment</param>
    /// <param name="currentValue">The current value</param>
    public static Margin Calculate(decimal cost, decimal currentValue)
    {
        if (cost == 0)
            throw new ArgumentException("Cost cannot be zero", nameof(cost));

        var percentage = ((currentValue - cost) / cost) * 100m;
        return new Margin(percentage);
    }

    /// <summary>
    /// Creates a zero margin.
    /// </summary>
    public static Margin Zero => new(0m);

    /// <summary>
    /// Checks if this represents a profit (positive margin).
    /// </summary>
    public bool IsProfit => Percentage > 0m;

    /// <summary>
    /// Checks if this represents a loss (negative margin).
    /// </summary>
    public bool IsLoss => Percentage < 0m;

    /// <summary>
    /// Checks if this is break-even (zero margin).
    /// </summary>
    public bool IsBreakEven => Percentage == 0m;

    /// <summary>
    /// Gets the absolute margin value.
    /// </summary>
    public Margin Abs() => new(Math.Abs(Percentage));

    /// <summary>
    /// Calculates the change from another margin.
    /// </summary>
    public Margin ChangeFrom(Margin other)
    {
        return new Margin(Percentage - other.Percentage);
    }

    /// <summary>
    /// Rounds the margin to the specified decimal places.
    /// </summary>
    public Margin Round(int decimals) => new(Math.Round(Percentage, decimals, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Applies this margin to a cost to get the resulting value.
    /// </summary>
    public decimal ApplyTo(decimal cost)
    {
        return cost * (1 + Percentage / 100m);
    }

    public static Margin operator +(Margin left, Margin right) => new(left.Percentage + right.Percentage);
    public static Margin operator -(Margin left, Margin right) => new(left.Percentage - right.Percentage);
    public static Margin operator -(Margin margin) => new(-margin.Percentage);

    public static bool operator >(Margin left, Margin right) => left.Percentage > right.Percentage;
    public static bool operator <(Margin left, Margin right) => left.Percentage < right.Percentage;
    public static bool operator >=(Margin left, Margin right) => left.Percentage >= right.Percentage;
    public static bool operator <=(Margin left, Margin right) => left.Percentage <= right.Percentage;

    public int CompareTo(Margin? other)
    {
        if (other is null) return 1;
        return Percentage.CompareTo(other.Percentage);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Percentage;
    }

    public override string ToString() => $"{Percentage:F2}%";

    public static implicit operator decimal(Margin margin) => margin.Percentage;
}
