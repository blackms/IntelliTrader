using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a new position is opened.
/// </summary>
public sealed record PositionOpened : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public OrderId OrderId { get; }
    public Price Price { get; }
    public Quantity Quantity { get; }
    public Money Cost { get; }
    public Money Fees { get; }
    public string? SignalRule { get; }
    public DateTimeOffset OccurredAt { get; }

    public PositionOpened(
        PositionId positionId,
        TradingPair pair,
        OrderId orderId,
        Price price,
        Quantity quantity,
        Money cost,
        Money fees,
        string? signalRule)
    {
        PositionId = positionId;
        Pair = pair;
        OrderId = orderId;
        Price = price;
        Quantity = quantity;
        Cost = cost;
        Fees = fees;
        SignalRule = signalRule;
        OccurredAt = DateTimeOffset.UtcNow;
    }
}
