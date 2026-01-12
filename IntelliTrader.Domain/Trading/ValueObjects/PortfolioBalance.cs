using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents the balance state of a portfolio, tracking available and reserved funds.
/// </summary>
public sealed class PortfolioBalance : ValueObject
{
    /// <summary>
    /// The currency of the balance.
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// The total balance (available + reserved).
    /// </summary>
    public Money Total { get; }

    /// <summary>
    /// The available balance for new trades.
    /// </summary>
    public Money Available { get; }

    /// <summary>
    /// The reserved balance (funds in open positions).
    /// </summary>
    public Money Reserved { get; }

    private PortfolioBalance(string currency, Money total, Money available, Money reserved)
    {
        Currency = currency;
        Total = total;
        Available = available;
        Reserved = reserved;
    }

    /// <summary>
    /// Creates a new portfolio balance.
    /// </summary>
    public static PortfolioBalance Create(decimal totalAmount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be null or empty", nameof(currency));

        if (totalAmount < 0)
            throw new ArgumentException("Total amount cannot be negative", nameof(totalAmount));

        currency = currency.ToUpperInvariant();
        var total = Money.Create(totalAmount, currency);
        var available = Money.Create(totalAmount, currency);
        var reserved = Money.Zero(currency);

        return new PortfolioBalance(currency, total, available, reserved);
    }

    /// <summary>
    /// Creates a zero balance for the specified currency.
    /// </summary>
    public static PortfolioBalance Zero(string currency)
    {
        return Create(0m, currency);
    }

    /// <summary>
    /// Checks if there's enough available balance for the specified amount.
    /// </summary>
    public bool CanAfford(Money amount)
    {
        if (amount.Currency != Currency)
            throw new InvalidOperationException($"Currency mismatch: expected {Currency}, got {amount.Currency}");

        return Available >= amount;
    }

    /// <summary>
    /// Reserves funds for a new position.
    /// </summary>
    public PortfolioBalance Reserve(Money amount)
    {
        if (amount.Currency != Currency)
            throw new InvalidOperationException($"Currency mismatch: expected {Currency}, got {amount.Currency}");

        if (!CanAfford(amount))
            throw new InvalidOperationException($"Insufficient funds. Available: {Available}, Required: {amount}");

        return new PortfolioBalance(
            Currency,
            Total,
            Available - amount,
            Reserved + amount);
    }

    /// <summary>
    /// Releases reserved funds (when a position is closed).
    /// </summary>
    public PortfolioBalance Release(Money amount)
    {
        if (amount.Currency != Currency)
            throw new InvalidOperationException($"Currency mismatch: expected {Currency}, got {amount.Currency}");

        if (amount > Reserved)
            throw new InvalidOperationException($"Cannot release more than reserved. Reserved: {Reserved}, Releasing: {amount}");

        return new PortfolioBalance(
            Currency,
            Total,
            Available + amount,
            Reserved - amount);
    }

    /// <summary>
    /// Updates the total balance (e.g., from exchange sync).
    /// </summary>
    public PortfolioBalance UpdateTotal(decimal newTotal)
    {
        if (newTotal < 0)
            throw new ArgumentException("Total cannot be negative", nameof(newTotal));

        var newTotalMoney = Money.Create(newTotal, Currency);

        // Calculate new available based on the difference
        var difference = newTotalMoney - Total;
        var newAvailable = Available + difference;

        // Ensure available doesn't go negative
        if (newAvailable.IsNegative)
        {
            // If new total is less than reserved, adjust reserved
            return new PortfolioBalance(
                Currency,
                newTotalMoney,
                Money.Zero(Currency),
                newTotalMoney);
        }

        return new PortfolioBalance(
            Currency,
            newTotalMoney,
            newAvailable,
            Reserved);
    }

    /// <summary>
    /// Records a profit or loss (adjusts total and available).
    /// </summary>
    public PortfolioBalance RecordPnL(Money pnl)
    {
        if (pnl.Currency != Currency)
            throw new InvalidOperationException($"Currency mismatch: expected {Currency}, got {pnl.Currency}");

        var newTotal = Total + pnl;
        var newAvailable = Available + pnl;

        // Clamp to zero if negative (shouldn't happen in normal operation)
        if (newTotal.IsNegative)
            newTotal = Money.Zero(Currency);
        if (newAvailable.IsNegative)
            newAvailable = Money.Zero(Currency);

        return new PortfolioBalance(Currency, newTotal, newAvailable, Reserved);
    }

    /// <summary>
    /// Returns the percentage of balance currently reserved.
    /// </summary>
    public decimal ReservedPercentage => Total.IsZero ? 0m : (Reserved.Amount / Total.Amount) * 100m;

    /// <summary>
    /// Returns the percentage of balance currently available.
    /// </summary>
    public decimal AvailablePercentage => Total.IsZero ? 0m : (Available.Amount / Total.Amount) * 100m;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Currency;
        yield return Total;
        yield return Available;
        yield return Reserved;
    }

    public override string ToString() => $"Total: {Total}, Available: {Available}, Reserved: {Reserved}";
}
