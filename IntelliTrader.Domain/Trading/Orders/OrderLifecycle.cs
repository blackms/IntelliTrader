using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Orders;

/// <summary>
/// Domain state machine for an order submitted to the exchange.
/// Tracks the lifecycle needed by the new Application handlers without
/// depending on transport-specific exchange statuses.
/// </summary>
public sealed class OrderLifecycle : AggregateRoot<OrderId>
{
    private OrderLifecycle() { }

    public TradingPair Pair { get; private set; } = null!;
    public OrderSide Side { get; private set; }
    public OrderType Type { get; private set; }
    public Quantity RequestedQuantity { get; private set; } = Quantity.Zero;
    public Quantity FilledQuantity { get; private set; } = Quantity.Zero;
    public Price SubmittedPrice { get; private set; } = Price.Zero;
    public Price AveragePrice { get; private set; } = Price.Zero;
    public Money Cost { get; private set; } = null!;
    public Money Fees { get; private set; } = null!;
    public Quantity AppliedQuantity { get; private set; } = Quantity.Zero;
    public Money AppliedCost { get; private set; } = null!;
    public Money AppliedFees { get; private set; } = null!;
    public string? SignalRule { get; private set; }
    public OrderIntent Intent { get; private set; }
    public PositionId? RelatedPositionId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public OrderLifecycleStatus Status { get; private set; }

    public bool CanAffectPosition =>
        Status is OrderLifecycleStatus.PartiallyFilled or OrderLifecycleStatus.Filled;

    public bool HasUnappliedFill =>
        CanAffectPosition && FilledQuantity > AppliedQuantity;

    public Quantity UnappliedQuantity => FilledQuantity - AppliedQuantity;

    public Money UnappliedCost => Cost - AppliedCost;

    public Money UnappliedFees => Fees - AppliedFees;

    public bool IsTerminal =>
        Status is OrderLifecycleStatus.Filled or OrderLifecycleStatus.Canceled or OrderLifecycleStatus.Rejected;

    public static OrderLifecycle Submit(
        OrderId orderId,
        TradingPair pair,
        OrderSide side,
        OrderType type,
        Quantity requestedQuantity,
        Price submittedPrice,
        string? signalRule = null,
        DateTimeOffset? timestamp = null,
        OrderIntent intent = OrderIntent.Unknown,
        PositionId? relatedPositionId = null)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(requestedQuantity);
        ArgumentNullException.ThrowIfNull(submittedPrice);

        if (requestedQuantity.IsZero)
            throw new ArgumentException("Requested quantity cannot be zero", nameof(requestedQuantity));

        if (intent is OrderIntent.ClosePosition or OrderIntent.ExecuteDca && relatedPositionId is null)
            throw new ArgumentException("Related position ID is required for close and DCA orders.", nameof(relatedPositionId));

        if (intent == OrderIntent.OpenPosition && relatedPositionId is not null)
            throw new ArgumentException("Open position orders cannot reference an existing position.", nameof(relatedPositionId));

        var submittedAt = timestamp ?? DateTimeOffset.UtcNow;
        var order = new OrderLifecycle
        {
            Id = orderId,
            Pair = pair,
            Side = side,
            Type = type,
            RequestedQuantity = requestedQuantity,
            SubmittedPrice = submittedPrice,
            AveragePrice = Price.Zero,
            FilledQuantity = Quantity.Zero,
            Cost = Money.Zero(pair.QuoteCurrency),
            Fees = Money.Zero(pair.QuoteCurrency),
            AppliedQuantity = Quantity.Zero,
            AppliedCost = Money.Zero(pair.QuoteCurrency),
            AppliedFees = Money.Zero(pair.QuoteCurrency),
            SignalRule = signalRule,
            Intent = intent,
            RelatedPositionId = relatedPositionId,
            SubmittedAt = submittedAt,
            Status = OrderLifecycleStatus.Submitted
        };

        order.AddDomainEvent(new OrderPlacedEvent(
            orderId.Value,
            pair.Symbol,
            side,
            requestedQuantity.Value,
            submittedPrice.Value,
            type,
            isManual: false,
            signalRule: signalRule));

        return order;
    }

    public void MarkPartiallyFilled(
        Quantity filledQuantity,
        Price averagePrice,
        Money cost,
        Money fees)
    {
        ApplyFill(OrderLifecycleStatus.PartiallyFilled, filledQuantity, averagePrice, cost, fees, isPartialFill: true);
    }

    public void MarkFilled(
        Quantity filledQuantity,
        Price averagePrice,
        Money cost,
        Money fees)
    {
        ApplyFill(OrderLifecycleStatus.Filled, filledQuantity, averagePrice, cost, fees, isPartialFill: false);
    }

    public void Cancel()
    {
        TransitionTo(OrderLifecycleStatus.Canceled);
    }

    public void Reject()
    {
        TransitionTo(OrderLifecycleStatus.Rejected);
    }

    public void MarkCurrentFillApplied()
    {
        if (!CanAffectPosition)
        {
            throw new InvalidOperationException(
                $"Cannot mark order {Id.Value} as applied because status {Status} has no fill.");
        }

        AppliedQuantity = FilledQuantity;
        AppliedCost = Cost;
        AppliedFees = Fees;
    }

    /// <summary>
    /// Links an open-position order to the position created from its first applied fill.
    /// </summary>
    public void LinkRelatedPosition(PositionId positionId)
    {
        ArgumentNullException.ThrowIfNull(positionId);

        if (Intent != OrderIntent.OpenPosition)
        {
            throw new InvalidOperationException(
                $"Only open position orders can be linked after submission. Order {Id.Value} has intent {Intent}.");
        }

        if (RelatedPositionId is not null && RelatedPositionId != positionId)
        {
            throw new InvalidOperationException(
                $"Order {Id.Value} is already linked to position {RelatedPositionId.Value}.");
        }

        RelatedPositionId = positionId;
    }

    private void ApplyFill(
        OrderLifecycleStatus targetStatus,
        Quantity filledQuantity,
        Price averagePrice,
        Money cost,
        Money fees,
        bool isPartialFill)
    {
        ArgumentNullException.ThrowIfNull(filledQuantity);
        ArgumentNullException.ThrowIfNull(averagePrice);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(fees);

        if (filledQuantity.IsZero)
            throw new ArgumentException("Filled quantity cannot be zero", nameof(filledQuantity));

        if (filledQuantity > RequestedQuantity)
            throw new ArgumentException("Filled quantity cannot exceed requested quantity.", nameof(filledQuantity));

        if (averagePrice.IsZero)
            throw new ArgumentException("Average price cannot be zero", nameof(averagePrice));

        if (Status == OrderLifecycleStatus.PartiallyFilled &&
            targetStatus is OrderLifecycleStatus.PartiallyFilled or OrderLifecycleStatus.Filled &&
            filledQuantity < FilledQuantity)
        {
            throw new InvalidOperationException("Cumulative filled quantity cannot be lower than the current filled quantity.");
        }

        if (Status == targetStatus && targetStatus == OrderLifecycleStatus.PartiallyFilled)
        {
            if (filledQuantity == FilledQuantity && averagePrice == AveragePrice && cost == Cost && fees == Fees)
                return;
        }
        else
        {
            TransitionTo(targetStatus);
        }

        FilledQuantity = filledQuantity;
        AveragePrice = averagePrice;
        Cost = cost;
        Fees = fees;

        AddDomainEvent(new OrderFilledEvent(
            Id.Value,
            Pair.Symbol,
            Side,
            filledQuantity.Value,
            averagePrice.Value,
            cost.Amount,
            fees.Amount,
            isPartialFill));
    }

    private void TransitionTo(OrderLifecycleStatus nextStatus)
    {
        if (!CanTransitionTo(nextStatus))
        {
            throw new InvalidOperationException(
                $"Cannot transition order {Id.Value} from {Status} to {nextStatus}.");
        }

        Status = nextStatus;
    }

    private bool CanTransitionTo(OrderLifecycleStatus nextStatus)
    {
        return Status switch
        {
            OrderLifecycleStatus.Submitted => nextStatus is
                OrderLifecycleStatus.PartiallyFilled or
                OrderLifecycleStatus.Filled or
                OrderLifecycleStatus.Canceled or
                OrderLifecycleStatus.Rejected,

            OrderLifecycleStatus.PartiallyFilled => nextStatus is
                OrderLifecycleStatus.Filled or
                OrderLifecycleStatus.Canceled,

            OrderLifecycleStatus.Filled => false,
            OrderLifecycleStatus.Canceled => false,
            OrderLifecycleStatus.Rejected => false,
            _ => false
        };
    }
}

public enum OrderLifecycleStatus
{
    Submitted,
    PartiallyFilled,
    Filled,
    Canceled,
    Rejected
}

public enum OrderIntent
{
    Unknown,
    OpenPosition,
    ClosePosition,
    ExecuteDca
}
