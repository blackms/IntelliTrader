using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a sell order partially reduces an open position.
/// </summary>
public sealed record PositionPartiallyClosed : IDomainEvent
{
    public Guid EventId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public OrderId SellOrderId { get; }
    public Price SellPrice { get; }
    public Quantity SoldQuantity { get; }
    public Quantity RemainingQuantity { get; }
    public Money Proceeds { get; }
    public Money ReleasedCost { get; }
    public Money SellFees { get; }
    public Money? RemainingCost { get; }
    public Money? RemainingFees { get; }
    public Price? NewAveragePrice { get; }
    public int? RemainingEntryCount { get; }

    public PositionPartiallyClosed(
        PositionId positionId,
        TradingPair pair,
        OrderId sellOrderId,
        Price sellPrice,
        Quantity soldQuantity,
        Quantity remainingQuantity,
        Money proceeds,
        Money releasedCost,
        Money sellFees,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default,
        Money? remainingCost = null,
        Money? remainingFees = null,
        Price? newAveragePrice = null,
        int? remainingEntryCount = null)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        PositionId = positionId;
        Pair = pair;
        SellOrderId = sellOrderId;
        SellPrice = sellPrice;
        SoldQuantity = soldQuantity;
        RemainingQuantity = remainingQuantity;
        Proceeds = proceeds;
        ReleasedCost = releasedCost;
        SellFees = sellFees;
        CorrelationId = correlationId;
        RemainingCost = remainingCost;
        RemainingFees = remainingFees;
        NewAveragePrice = newAveragePrice;
        RemainingEntryCount = remainingEntryCount;
    }
}
