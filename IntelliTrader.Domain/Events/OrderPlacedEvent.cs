using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when an order is placed on the exchange.
/// </summary>
public sealed record OrderPlacedEvent : IDomainEvent
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
    /// The amount/quantity of the order.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// The price at which the order was placed.
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// The order type (Market, Limit, etc.).
    /// </summary>
    public OrderType OrderType { get; }

    /// <summary>
    /// Whether this is a manual order (vs automated).
    /// </summary>
    public bool IsManual { get; }

    /// <summary>
    /// The signal rule that triggered this order, if any.
    /// </summary>
    public string? SignalRule { get; }

    public OrderPlacedEvent(
        string orderId,
        string pair,
        OrderSide side,
        decimal amount,
        decimal price,
        OrderType orderType = OrderType.Limit,
        bool isManual = false,
        string? signalRule = null,
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
        Amount = amount;
        Price = price;
        OrderType = orderType;
        IsManual = isManual;
        SignalRule = signalRule;
    }
}

/// <summary>
/// The side of an order.
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// The type of an order.
/// </summary>
public enum OrderType
{
    Market,
    Limit,
    StopLoss,
    TakeProfit
}
