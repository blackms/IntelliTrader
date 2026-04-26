using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

/// <summary>
/// Initial order read-model adapter backed by the persisted order lifecycle store.
/// </summary>
public sealed class RepositoryOrderReadModel : IOrderReadModel
{
    private readonly IOrderRepository _orderRepository;

    public RepositoryOrderReadModel(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<OrderView?> GetByIdAsync(
        OrderId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var order = await _orderRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return order is null ? null : MapOrderView(order);
    }

    public async Task<IReadOnlyList<OrderView>> GetRecentAsync(
        TradingPair? pair,
        OrderLifecycleStatus? status,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = limit <= 0 ? 50 : limit;
        var orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return orders
            .Where(order => pair is null || order.Pair.Equals(pair))
            .Where(order => status is null || order.Status == status)
            .Where(order => side is null || order.Side == side)
            .OrderByDescending(order => order.SubmittedAt)
            .Take(safeLimit)
            .Select(MapOrderView)
            .ToList();
    }

    public async Task<IReadOnlyList<OrderView>> GetActiveAsync(
        TradingPair? pair,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = limit <= 0 ? 50 : limit;
        var orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return orders
            .Where(order => !order.IsTerminal)
            .Where(order => pair is null || order.Pair.Equals(pair))
            .Where(order => side is null || order.Side == side)
            .OrderByDescending(order => order.SubmittedAt)
            .Take(safeLimit)
            .Select(MapOrderView)
            .ToList();
    }

    public async Task<IReadOnlyList<TradeHistoryEntry>> GetTradingHistoryAsync(
        TradingPair? pair,
        DateTimeOffset from,
        DateTimeOffset to,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = limit <= 0 ? 100 : limit;
        var safeOffset = Math.Max(0, offset);
        var orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return orders
            .Where(order => order.Status == OrderLifecycleStatus.Filled)
            .Where(order => order.RelatedPositionId is not null)
            .Where(order => order.SubmittedAt >= from && order.SubmittedAt <= to)
            .Where(order => pair is null || order.Pair.Equals(pair))
            .OrderByDescending(order => order.SubmittedAt)
            .Skip(safeOffset)
            .Take(safeLimit)
            .Select(MapTradeHistoryEntry)
            .ToList();
    }

    private static OrderView MapOrderView(OrderLifecycle order)
    {
        return new OrderView
        {
            Id = order.Id,
            Pair = order.Pair,
            Side = order.Side,
            Type = order.Type,
            Status = order.Status,
            RequestedQuantity = order.RequestedQuantity,
            FilledQuantity = order.FilledQuantity,
            SubmittedPrice = order.SubmittedPrice,
            AveragePrice = order.AveragePrice,
            Cost = order.Cost,
            Fees = order.Fees,
            SignalRule = order.SignalRule,
            SubmittedAt = order.SubmittedAt,
            CanAffectPosition = order.CanAffectPosition,
            IsTerminal = order.IsTerminal
        };
    }

    private static TradeHistoryEntry MapTradeHistoryEntry(OrderLifecycle order)
    {
        return new TradeHistoryEntry
        {
            PositionId = order.RelatedPositionId!,
            Pair = order.Pair,
            Type = ResolveTradeType(order),
            Price = order.AveragePrice,
            Quantity = order.FilledQuantity,
            Cost = order.Cost,
            Fees = order.Fees,
            Timestamp = order.SubmittedAt,
            OrderId = order.Id.Value,
            Note = order.Intent.ToString()
        };
    }

    private static TradeType ResolveTradeType(OrderLifecycle order)
    {
        if (order.Side == DomainOrderSide.Sell)
        {
            return TradeType.Sell;
        }

        return order.Intent == OrderIntent.ExecuteDca ? TradeType.DCA : TradeType.Buy;
    }
}
