using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using StatusOrderSide = IntelliTrader.Application.Trading.Queries.OrderSide;
using StatusOrderStatus = IntelliTrader.Application.Trading.Queries.OrderStatus;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Query handler for portfolio status projected from read models.
/// </summary>
public sealed class GetPortfolioStatusHandler : IQueryHandler<GetPortfolioStatusQuery, GetPortfolioStatusResult>
{
    private readonly IPortfolioReadModel _portfolioReadModel;
    private readonly IPositionReadModel _positionReadModel;
    private readonly IOrderReadModel _orderReadModel;
    private readonly ITradingStateReadModel _tradingStateReadModel;
    private readonly IExchangePort _exchangePort;

    public GetPortfolioStatusHandler(
        IPortfolioReadModel portfolioReadModel,
        IPositionReadModel positionReadModel,
        IOrderReadModel orderReadModel,
        ITradingStateReadModel tradingStateReadModel,
        IExchangePort exchangePort)
    {
        _portfolioReadModel = portfolioReadModel ?? throw new ArgumentNullException(nameof(portfolioReadModel));
        _positionReadModel = positionReadModel ?? throw new ArgumentNullException(nameof(positionReadModel));
        _orderReadModel = orderReadModel ?? throw new ArgumentNullException(nameof(orderReadModel));
        _tradingStateReadModel = tradingStateReadModel ?? throw new ArgumentNullException(nameof(tradingStateReadModel));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    public async Task<Result<GetPortfolioStatusResult>> HandleAsync(
        GetPortfolioStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var portfolio = await _portfolioReadModel.GetDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return Result<GetPortfolioStatusResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        var activePositions = await _positionReadModel.GetActiveAsync(
            portfolio.Market,
            cancellationToken).ConfigureAwait(false);

        var priceResult = await LoadPricesAsync(activePositions, cancellationToken).ConfigureAwait(false);
        if (priceResult.IsFailure)
        {
            return Result<GetPortfolioStatusResult>.Failure(priceResult.Error);
        }

        var tradingState = await _tradingStateReadModel.GetAsync(cancellationToken).ConfigureAwait(false);
        var activeOrders = query.IncludeTrailingOrders
            ? await _orderReadModel.GetActiveAsync(
                pair: null,
                side: null,
                limit: int.MaxValue,
                cancellationToken).ConfigureAwait(false)
            : Array.Empty<OrderView>();
        var trailingBuys = query.IncludeTrailingOrders ? MapActiveOrderPairs(activeOrders, DomainOrderSide.Buy) : null;
        var trailingSells = query.IncludeTrailingOrders ? MapActiveOrderPairs(activeOrders, DomainOrderSide.Sell) : null;
        var trailingSellPairs = trailingSells?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = activePositions
            .Select(position => PositionStatusSnapshot.Create(
                position,
                priceResult.Value[position.Pair],
                trailingSellPairs.Contains(position.Pair.Symbol)))
            .ToList();

        IReadOnlyList<OrderHistoryInfo>? orderHistory = null;
        if (query.IncludeOrderHistory)
        {
            var limit = query.OrderHistoryLimit <= 0 ? 50 : query.OrderHistoryLimit;
            var recentOrders = await _orderReadModel.GetRecentAsync(
                pair: null,
                status: null,
                side: null,
                limit,
                cancellationToken).ConfigureAwait(false);
            orderHistory = recentOrders.Select(MapOrderHistory).ToList();
        }

        var currentValue = snapshots.Sum(snapshot => snapshot.CurrentValue.Amount);
        var unrealizedPnL = snapshots.Sum(snapshot => snapshot.UnrealizedPnL.Amount);
        var costBasis = snapshots.Sum(snapshot => snapshot.CostBasis.Amount);

        return Result<GetPortfolioStatusResult>.Success(new GetPortfolioStatusResult
        {
            Summary = new PortfolioSummary
            {
                TotalBalance = portfolio.TotalBalance.Amount,
                AvailableBalance = portfolio.AvailableBalance.Amount,
                ReservedBalance = portfolio.ReservedBalance.Amount,
                CurrentValue = currentValue,
                UnrealizedPnL = unrealizedPnL,
                UnrealizedPnLPercent = costBasis == 0m ? 0m : unrealizedPnL / costBasis * 100m,
                ActivePositionCount = portfolio.ActivePositionCount,
                TrailingBuyCount = trailingBuys?.Count ?? 0,
                TrailingSellCount = trailingSells?.Count ?? 0,
                IsTradingSuspended = tradingState.IsTradingSuspended,
                Market = portfolio.Market
            },
            Positions = query.IncludePositions
                ? snapshots.Select(snapshot => snapshot.ToPositionInfo()).ToList()
                : null,
            TrailingBuys = trailingBuys,
            TrailingSells = trailingSells,
            OrderHistory = orderHistory
        });
    }

    private async Task<Result<IReadOnlyDictionary<TradingPair, Price>>> LoadPricesAsync(
        IReadOnlyCollection<PositionReadModelEntry> positions,
        CancellationToken cancellationToken)
    {
        if (positions.Count == 0)
        {
            return Result<IReadOnlyDictionary<TradingPair, Price>>.Success(
                new Dictionary<TradingPair, Price>());
        }

        var pairs = positions.Select(position => position.Pair).Distinct().ToList();
        var priceResult = await _exchangePort.GetCurrentPricesAsync(pairs, cancellationToken).ConfigureAwait(false);
        if (priceResult.IsFailure)
        {
            return Result<IReadOnlyDictionary<TradingPair, Price>>.Failure(priceResult.Error);
        }

        foreach (var pair in pairs)
        {
            if (!priceResult.Value.ContainsKey(pair))
            {
                return Result<IReadOnlyDictionary<TradingPair, Price>>.Failure(
                    Error.ExchangeError($"Missing current price for {pair.Symbol}."));
            }
        }

        return priceResult;
    }

    private static OrderHistoryInfo MapOrderHistory(OrderView order)
    {
        return new OrderHistoryInfo
        {
            OrderId = order.Id.Value,
            Pair = order.Pair.Symbol,
            Side = MapSide(order.Side),
            Status = MapStatus(order.Status),
            Amount = order.RequestedQuantity.Value,
            AmountFilled = order.FilledQuantity.Value,
            Price = order.SubmittedPrice.Value,
            AveragePrice = order.AveragePrice.Value,
            TotalCost = order.Cost.Amount,
            Fees = order.Fees.Amount,
            Timestamp = order.SubmittedAt,
            Message = order.SignalRule
        };
    }

    private static IReadOnlyList<string> MapActiveOrderPairs(
        IReadOnlyList<OrderView> activeOrders,
        DomainOrderSide side)
    {
        return activeOrders
            .Where(order => order.Side == side)
            .Select(order => order.Pair.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pair => pair, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static StatusOrderSide MapSide(DomainOrderSide side)
    {
        return side == DomainOrderSide.Sell ? StatusOrderSide.Sell : StatusOrderSide.Buy;
    }

    private static StatusOrderStatus MapStatus(OrderLifecycleStatus status)
    {
        return status switch
        {
            OrderLifecycleStatus.Filled => StatusOrderStatus.Filled,
            OrderLifecycleStatus.PartiallyFilled => StatusOrderStatus.PartiallyFilled,
            OrderLifecycleStatus.Canceled => StatusOrderStatus.Canceled,
            OrderLifecycleStatus.Rejected => StatusOrderStatus.Rejected,
            OrderLifecycleStatus.Submitted => StatusOrderStatus.Pending,
            _ => StatusOrderStatus.Unknown
        };
    }

    private sealed record PositionStatusSnapshot(
        PositionReadModelEntry Position,
        Price CurrentPrice,
        Money CurrentValue,
        Money UnrealizedPnL,
        Money CostBasis,
        Margin Margin,
        bool HasTrailingSell)
    {
        public static PositionStatusSnapshot Create(
            PositionReadModelEntry position,
            Price currentPrice,
            bool hasTrailingSell)
        {
            var currentValue = Money.Create(
                currentPrice.Value * position.TotalQuantity.Value,
                position.TotalCost.Currency);
            var costBasis = position.TotalCost + position.TotalFees;
            var unrealizedPnL = currentValue - costBasis;
            var margin = costBasis.Amount == 0m
                ? Margin.Zero
                : Margin.Calculate(costBasis.Amount, currentValue.Amount);

            return new PositionStatusSnapshot(
                position,
                currentPrice,
                currentValue,
                unrealizedPnL,
                costBasis,
                margin,
                hasTrailingSell);
        }

        public PositionInfo ToPositionInfo()
        {
            return new PositionInfo
            {
                Pair = Position.Pair.Symbol,
                Amount = Position.TotalQuantity.Value,
                TotalCost = Position.TotalCost.Amount,
                CurrentValue = CurrentValue.Amount,
                AveragePrice = Position.AveragePrice.Value,
                CurrentPrice = CurrentPrice.Value,
                UnrealizedPnL = UnrealizedPnL.Amount,
                Margin = Margin.Percentage,
                DCALevel = Position.DCALevel,
                Age = DateTimeOffset.UtcNow - Position.OpenedAt,
                OpenedAt = Position.OpenedAt,
                SignalRule = Position.SignalRule,
                HasTrailingSell = HasTrailingSell
            };
        }
    }
}
