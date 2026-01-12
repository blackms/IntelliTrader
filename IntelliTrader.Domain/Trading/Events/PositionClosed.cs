using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a position is closed.
/// </summary>
public sealed record PositionClosed : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public OrderId SellOrderId { get; }
    public Price SellPrice { get; }
    public Quantity SoldQuantity { get; }
    public Money Proceeds { get; }
    public Money TotalCost { get; }
    public Money TotalFees { get; }
    public Margin FinalMargin { get; }
    public int DCALevel { get; }
    public TimeSpan Duration { get; }
    public DateTimeOffset OccurredAt { get; }

    public PositionClosed(
        PositionId positionId,
        TradingPair pair,
        OrderId sellOrderId,
        Price sellPrice,
        Quantity soldQuantity,
        Money proceeds,
        Money totalCost,
        Money totalFees,
        Margin finalMargin,
        int dcaLevel,
        TimeSpan duration)
    {
        PositionId = positionId;
        Pair = pair;
        SellOrderId = sellOrderId;
        SellPrice = sellPrice;
        SoldQuantity = soldQuantity;
        Proceeds = proceeds;
        TotalCost = totalCost;
        TotalFees = totalFees;
        FinalMargin = finalMargin;
        DCALevel = dcaLevel;
        Duration = duration;
        OccurredAt = DateTimeOffset.UtcNow;
    }
}
