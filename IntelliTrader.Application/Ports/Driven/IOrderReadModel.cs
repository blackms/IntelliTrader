using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Read-side port for order query models.
/// </summary>
public interface IOrderReadModel
{
    Task<OrderView?> GetByIdAsync(
        OrderId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetRecentAsync(
        TradingPair? pair,
        OrderLifecycleStatus? status,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetActiveAsync(
        TradingPair? pair,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeHistoryEntry>> GetTradingHistoryAsync(
        TradingPair? pair,
        DateTimeOffset from,
        DateTimeOffset to,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
