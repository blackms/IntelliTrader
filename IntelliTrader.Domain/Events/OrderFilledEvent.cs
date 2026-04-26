using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when an order is fully or partially filled.
/// </summary>
public sealed record OrderFilledEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// The exchange-assigned order identifier.
    /// </summary>
    public string OrderId { get; }

    /// <summary>
    /// The trading pair (e.g., "BTCUSDT").
    /// </summary>
    public string Pair { get; }

    /// <summary>
    /// The order side (Buy or Sell).
    /// </summary>
    public OrderSide Side { get; }

    /// <summary>
    /// The amount that was filled.
    /// </summary>
    public decimal FilledAmount { get; }

    /// <summary>
    /// The average price at which the order was filled.
    /// </summary>
    public decimal AveragePrice { get; }

    /// <summary>
    /// The total cost of the filled order (FilledAmount * AveragePrice).
    /// </summary>
    public decimal Cost { get; }

    /// <summary>
    /// The fees paid for this order.
    /// </summary>
    public decimal Fees { get; }

    /// <summary>
    /// Whether the order was partially filled (vs fully filled).
    /// </summary>
    public bool IsPartialFill { get; }

    public OrderFilledEvent(
        string orderId,
        string pair,
        OrderSide side,
        decimal filledAmount,
        decimal averagePrice,
        decimal cost,
        decimal fees = 0,
        bool isPartialFill = false,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
        OrderId = orderId;
        Pair = pair;
        Side = side;
        FilledAmount = filledAmount;
        AveragePrice = averagePrice;
        Cost = cost;
        Fees = fees;
        IsPartialFill = isPartialFill;
    }
}
