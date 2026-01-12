using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Signals.ValueObjects;

/// <summary>
/// Represents a signal rating value, typically between -1 and 1.
/// Higher values indicate stronger buy signals, lower values indicate sell signals.
/// </summary>
public sealed class SignalRating : ValueObject, IComparable<SignalRating>
{
    /// <summary>
    /// Minimum valid rating value.
    /// </summary>
    public const double MinValue = -1.0;

    /// <summary>
    /// Maximum valid rating value.
    /// </summary>
    public const double MaxValue = 1.0;

    /// <summary>
    /// The rating value.
    /// </summary>
    public double Value { get; }

    private SignalRating(double value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a SignalRating instance.
    /// </summary>
    /// <param name="value">The rating value (typically between -1 and 1)</param>
    /// <param name="clamp">If true, clamps the value to valid range instead of throwing</param>
    public static SignalRating Create(double value, bool clamp = false)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException("Rating value must be a finite number", nameof(value));

        if (clamp)
        {
            value = Math.Clamp(value, MinValue, MaxValue);
        }
        else if (value < MinValue || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Rating must be between {MinValue} and {MaxValue}");
        }

        return new SignalRating(value);
    }

    /// <summary>
    /// Creates a neutral rating (0).
    /// </summary>
    public static SignalRating Neutral => new(0.0);

    /// <summary>
    /// Creates a strong buy rating (1).
    /// </summary>
    public static SignalRating StrongBuy => new(MaxValue);

    /// <summary>
    /// Creates a strong sell rating (-1).
    /// </summary>
    public static SignalRating StrongSell => new(MinValue);

    /// <summary>
    /// Checks if this is a buy signal (positive rating).
    /// </summary>
    public bool IsBuySignal => Value > 0;

    /// <summary>
    /// Checks if this is a sell signal (negative rating).
    /// </summary>
    public bool IsSellSignal => Value < 0;

    /// <summary>
    /// Checks if this is a neutral signal (zero or near-zero rating).
    /// </summary>
    /// <param name="tolerance">The tolerance for considering a value neutral</param>
    public bool IsNeutral(double tolerance = 0.01) => Math.Abs(Value) <= tolerance;

    /// <summary>
    /// Checks if this is a strong signal (above threshold).
    /// </summary>
    /// <param name="threshold">The threshold for considering a signal strong (default 0.5)</param>
    public bool IsStrong(double threshold = 0.5) => Math.Abs(Value) >= threshold;

    /// <summary>
    /// Calculates the change from another rating.
    /// </summary>
    public double ChangeFrom(SignalRating other)
    {
        return Value - other.Value;
    }

    /// <summary>
    /// Returns the absolute strength of the signal.
    /// </summary>
    public double Strength => Math.Abs(Value);

    /// <summary>
    /// Rounds the rating to the specified decimal places.
    /// </summary>
    public SignalRating Round(int decimals) => new(Math.Round(Value, decimals, MidpointRounding.AwayFromZero));

    public static bool operator >(SignalRating left, SignalRating right) => left.Value > right.Value;
    public static bool operator <(SignalRating left, SignalRating right) => left.Value < right.Value;
    public static bool operator >=(SignalRating left, SignalRating right) => left.Value >= right.Value;
    public static bool operator <=(SignalRating left, SignalRating right) => left.Value <= right.Value;

    public int CompareTo(SignalRating? other)
    {
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString("F4");

    public static implicit operator double(SignalRating rating) => rating.Value;
}
