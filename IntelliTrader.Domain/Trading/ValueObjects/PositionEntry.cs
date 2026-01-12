using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a single buy entry in a position (initial buy or DCA).
/// </summary>
public sealed class PositionEntry : ValueObject
{
    /// <summary>
    /// The exchange order ID for this entry.
    /// </summary>
    public OrderId OrderId { get; }

    /// <summary>
    /// The price at which the asset was bought.
    /// </summary>
    public Price Price { get; }

    /// <summary>
    /// The quantity of the asset bought.
    /// </summary>
    public Quantity Quantity { get; }

    /// <summary>
    /// The fees paid for this entry.
    /// </summary>
    public Money Fees { get; }

    /// <summary>
    /// The timestamp when this entry was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// The cost of this entry (price * quantity).
    /// </summary>
    public Money Cost { get; }

    private PositionEntry(OrderId orderId, Price price, Quantity quantity, Money fees, DateTimeOffset timestamp)
    {
        OrderId = orderId;
        Price = price;
        Quantity = quantity;
        Fees = fees;
        Timestamp = timestamp;
        Cost = Money.Create(price.Value * quantity.Value, fees.Currency);
    }

    /// <summary>
    /// Creates a new position entry.
    /// </summary>
    public static PositionEntry Create(
        OrderId orderId,
        Price price,
        Quantity quantity,
        Money fees,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(fees);

        if (price.IsZero)
            throw new ArgumentException("Price cannot be zero", nameof(price));

        if (quantity.IsZero)
            throw new ArgumentException("Quantity cannot be zero", nameof(quantity));

        return new PositionEntry(orderId, price, quantity, fees, timestamp ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Calculates the current value of this entry at the given price.
    /// </summary>
    public Money CalculateCurrentValue(Price currentPrice)
    {
        return Money.Create(currentPrice.Value * Quantity.Value, Fees.Currency);
    }

    /// <summary>
    /// Calculates the margin (profit/loss percentage) at the given price.
    /// </summary>
    public Margin CalculateMargin(Price currentPrice)
    {
        var currentValue = currentPrice.Value * Quantity.Value;
        var costWithFees = Cost.Amount + Fees.Amount;

        if (costWithFees == 0)
            return Margin.Zero;

        return Margin.Calculate(costWithFees, currentValue);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OrderId;
        yield return Price;
        yield return Quantity;
        yield return Fees;
        yield return Timestamp;
    }
}
