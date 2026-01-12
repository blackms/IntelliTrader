using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a monetary amount with a currency.
/// Immutable value object with proper decimal handling for financial calculations.
/// </summary>
public sealed class Money : ValueObject, IComparable<Money>
{
    /// <summary>
    /// The monetary amount.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// The currency code (e.g., "USDT", "BTC").
    /// </summary>
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>
    /// Creates a Money instance.
    /// </summary>
    public static Money Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be null or empty", nameof(currency));

        return new Money(amount, currency.ToUpperInvariant());
    }

    /// <summary>
    /// Creates a zero Money instance for the specified currency.
    /// </summary>
    public static Money Zero(string currency) => Create(0m, currency);

    /// <summary>
    /// Checks if the amount is zero.
    /// </summary>
    public bool IsZero => Amount == 0m;

    /// <summary>
    /// Checks if the amount is positive.
    /// </summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>
    /// Checks if the amount is negative.
    /// </summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>
    /// Returns the absolute value.
    /// </summary>
    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>
    /// Returns the negated value.
    /// </summary>
    public Money Negate() => new(-Amount, Currency);

    /// <summary>
    /// Rounds the amount to the specified decimal places.
    /// </summary>
    public Money Round(int decimals) => new(Math.Round(Amount, decimals, MidpointRounding.AwayFromZero), Currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal multiplier)
    {
        return new Money(money.Amount * multiplier, money.Currency);
    }

    public static Money operator *(decimal multiplier, Money money)
    {
        return money * multiplier;
    }

    public static Money operator /(Money money, decimal divisor)
    {
        if (divisor == 0m)
            throw new DivideByZeroException("Cannot divide money by zero");

        return new Money(money.Amount / divisor, money.Currency);
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    public int CompareTo(Money? other)
    {
        if (other is null) return 1;
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!left.Currency.Equals(right.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot operate on different currencies: {left.Currency} and {right.Currency}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount} {Currency}";
}
