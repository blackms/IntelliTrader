using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Query handler for a single persisted order lifecycle.
/// </summary>
public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderView>
{
    private readonly IOrderReadModel _orderReadModel;

    public GetOrderHandler(IOrderReadModel orderReadModel)
    {
        _orderReadModel = orderReadModel ?? throw new ArgumentNullException(nameof(orderReadModel));
    }

    public async Task<Result<OrderView>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orderView = await _orderReadModel.GetByIdAsync(query.OrderId, cancellationToken);
        if (orderView is null)
        {
            return Result<OrderView>.Failure(Error.NotFound("Order", query.OrderId.Value));
        }

        return Result<OrderView>.Success(orderView);
    }
}

/// <summary>
/// Query handler for recent persisted orders.
/// </summary>
public sealed class GetRecentOrdersHandler : IQueryHandler<GetRecentOrdersQuery, IReadOnlyList<OrderView>>
{
    private readonly IOrderReadModel _orderReadModel;

    public GetRecentOrdersHandler(IOrderReadModel orderReadModel)
    {
        _orderReadModel = orderReadModel ?? throw new ArgumentNullException(nameof(orderReadModel));
    }

    public async Task<Result<IReadOnlyList<OrderView>>> HandleAsync(
        GetRecentOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 50 : query.Limit;
        var filtered = await _orderReadModel.GetRecentAsync(
            query.Pair,
            query.Status,
            query.Side,
            limit,
            cancellationToken);

        return Result<IReadOnlyList<OrderView>>.Success(filtered);
    }
}

/// <summary>
/// Query handler for active, non-terminal persisted orders.
/// </summary>
public sealed class GetActiveOrdersHandler : IQueryHandler<GetActiveOrdersQuery, IReadOnlyList<OrderView>>
{
    private readonly IOrderReadModel _orderReadModel;

    public GetActiveOrdersHandler(IOrderReadModel orderReadModel)
    {
        _orderReadModel = orderReadModel ?? throw new ArgumentNullException(nameof(orderReadModel));
    }

    public async Task<Result<IReadOnlyList<OrderView>>> HandleAsync(
        GetActiveOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 50 : query.Limit;
        var filtered = await _orderReadModel.GetActiveAsync(
            query.Pair,
            query.Side,
            limit,
            cancellationToken);

        return Result<IReadOnlyList<OrderView>>.Success(filtered);
    }
}

/// <summary>
/// Query handler for trade history projected from persisted filled order lifecycles.
/// </summary>
public sealed class GetTradingHistoryHandler : IQueryHandler<GetTradingHistoryQuery, IReadOnlyList<TradeHistoryEntry>>
{
    private readonly IOrderReadModel _orderReadModel;

    public GetTradingHistoryHandler(IOrderReadModel orderReadModel)
    {
        _orderReadModel = orderReadModel ?? throw new ArgumentNullException(nameof(orderReadModel));
    }

    public async Task<Result<IReadOnlyList<TradeHistoryEntry>>> HandleAsync(
        GetTradingHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.From > query.To)
        {
            return Result<IReadOnlyList<TradeHistoryEntry>>.Failure(
                Error.Validation("Trading history query range is invalid."));
        }

        var limit = query.Limit <= 0 ? 100 : query.Limit;
        var offset = Math.Max(0, query.Offset);
        var history = await _orderReadModel.GetTradingHistoryAsync(
            query.Pair,
            query.From,
            query.To,
            offset,
            limit,
            cancellationToken);

        return Result<IReadOnlyList<TradeHistoryEntry>>.Success(history);
    }
}
