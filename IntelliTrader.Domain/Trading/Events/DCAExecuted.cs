using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a DCA (Dollar Cost Average) buy is executed.
/// </summary>
public sealed record DCAExecuted : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public OrderId OrderId { get; }
    public int NewDCALevel { get; }
    public Price Price { get; }
    public Quantity Quantity { get; }
    public Money Cost { get; }
    public Money Fees { get; }
    public Price NewAveragePrice { get; }
    public Quantity NewTotalQuantity { get; }
    public Money NewTotalCost { get; }
    public DateTimeOffset OccurredAt { get; }

    public DCAExecuted(
        PositionId positionId,
        TradingPair pair,
        OrderId orderId,
        int newDCALevel,
        Price price,
        Quantity quantity,
        Money cost,
        Money fees,
        Price newAveragePrice,
        Quantity newTotalQuantity,
        Money newTotalCost)
    {
        PositionId = positionId;
        Pair = pair;
        OrderId = orderId;
        NewDCALevel = newDCALevel;
        Price = price;
        Quantity = quantity;
        Cost = cost;
        Fees = fees;
        NewAveragePrice = newAveragePrice;
        NewTotalQuantity = newTotalQuantity;
        NewTotalCost = newTotalCost;
        OccurredAt = DateTimeOffset.UtcNow;
    }
}
