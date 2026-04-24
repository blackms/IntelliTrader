using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Query handler for a single persisted order lifecycle.
/// </summary>
public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderView>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<Result<OrderView>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await _orderRepository.GetByIdAsync(query.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderView>.Failure(Error.NotFound("Order", query.OrderId.Value));
        }

        return Result<OrderView>.Success(Map(order));
    }

    internal static OrderView Map(OrderLifecycle order)
    {
        ArgumentNullException.ThrowIfNull(order);

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
}

/// <summary>
/// Query handler for recent persisted orders.
/// </summary>
public sealed class GetRecentOrdersHandler : IQueryHandler<GetRecentOrdersQuery, IReadOnlyList<OrderView>>
{
    private readonly IOrderRepository _orderRepository;

    public GetRecentOrdersHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<Result<IReadOnlyList<OrderView>>> HandleAsync(
        GetRecentOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 50 : query.Limit;
        var orders = await _orderRepository.GetAllAsync(cancellationToken);

        var filtered = orders
            .Where(order => query.Pair is null || order.Pair.Equals(query.Pair))
            .Where(order => query.Status is null || order.Status == query.Status)
            .Where(order => query.Side is null || order.Side == query.Side)
            .OrderByDescending(order => order.SubmittedAt)
            .Take(limit)
            .Select(GetOrderHandler.Map)
            .ToList();

        return Result<IReadOnlyList<OrderView>>.Success(filtered);
    }
}

/// <summary>
/// Query handler for active, non-terminal persisted orders.
/// </summary>
public sealed class GetActiveOrdersHandler : IQueryHandler<GetActiveOrdersQuery, IReadOnlyList<OrderView>>
{
    private readonly IOrderRepository _orderRepository;

    public GetActiveOrdersHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<Result<IReadOnlyList<OrderView>>> HandleAsync(
        GetActiveOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 50 : query.Limit;
        var orders = await _orderRepository.GetAllAsync(cancellationToken);

        var filtered = orders
            .Where(order => !order.IsTerminal)
            .Where(order => query.Pair is null || order.Pair.Equals(query.Pair))
            .Where(order => query.Side is null || order.Side == query.Side)
            .OrderByDescending(order => order.SubmittedAt)
            .Take(limit)
            .Select(GetOrderHandler.Map)
            .ToList();

        return Result<IReadOnlyList<OrderView>>.Success(filtered);
    }
}
