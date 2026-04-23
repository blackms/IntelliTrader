using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Queries;

/// <summary>
/// Query to get a single persisted order lifecycle.
/// </summary>
public sealed record GetOrderQuery
{
    public required OrderId OrderId { get; init; }
}

/// <summary>
/// Query to get recent persisted orders with optional filters.
/// </summary>
public sealed record GetRecentOrdersQuery
{
    public TradingPair? Pair { get; init; }
    public OrderLifecycleStatus? Status { get; init; }
    public IntelliTrader.Domain.Events.OrderSide? Side { get; init; }
    public int Limit { get; init; } = 50;
}

/// <summary>
/// Read model for an order lifecycle.
/// </summary>
public sealed record OrderView
{
    public required OrderId Id { get; init; }
    public required TradingPair Pair { get; init; }
    public required IntelliTrader.Domain.Events.OrderSide Side { get; init; }
    public required IntelliTrader.Domain.Events.OrderType Type { get; init; }
    public required OrderLifecycleStatus Status { get; init; }
    public required Quantity RequestedQuantity { get; init; }
    public required Quantity FilledQuantity { get; init; }
    public required Price SubmittedPrice { get; init; }
    public required Price AveragePrice { get; init; }
    public required Money Cost { get; init; }
    public required Money Fees { get; init; }
    public string? SignalRule { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required bool CanAffectPosition { get; init; }
    public required bool IsTerminal { get; init; }
}
