using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Query handler for portfolio read models.
/// </summary>
public sealed class GetPortfolioHandler : IQueryHandler<GetPortfolioQuery, PortfolioView>
{
    private readonly IPortfolioReadModel _portfolioReadModel;

    public GetPortfolioHandler(IPortfolioReadModel portfolioReadModel)
    {
        _portfolioReadModel = portfolioReadModel ?? throw new ArgumentNullException(nameof(portfolioReadModel));
    }

    public async Task<Result<PortfolioView>> HandleAsync(
        GetPortfolioQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var portfolio = await ResolvePortfolioAsync(query, cancellationToken);
        if (portfolio is null)
        {
            var id = query.PortfolioId?.ToString() ?? query.Name ?? "default";
            return Result<PortfolioView>.Failure(Error.NotFound("Portfolio", id));
        }

        return Result<PortfolioView>.Success(Map(portfolio));
    }

    private Task<PortfolioReadModelEntry?> ResolvePortfolioAsync(
        GetPortfolioQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PortfolioId is not null)
        {
            return _portfolioReadModel.GetByIdAsync(query.PortfolioId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            return _portfolioReadModel.GetByNameAsync(query.Name, cancellationToken);
        }

        return _portfolioReadModel.GetDefaultAsync(cancellationToken);
    }

    internal static PortfolioView Map(PortfolioReadModelEntry portfolio)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        return new PortfolioView
        {
            Id = portfolio.Id,
            Name = portfolio.Name,
            Market = portfolio.Market,
            TotalBalance = portfolio.TotalBalance,
            AvailableBalance = portfolio.AvailableBalance,
            ReservedBalance = portfolio.ReservedBalance,
            ActivePositionCount = portfolio.ActivePositionCount,
            MaxPositions = portfolio.MaxPositions,
            MinPositionCost = portfolio.MinPositionCost,
            CanOpenNewPosition = portfolio.CanOpenNewPosition,
            AvailablePercentage = portfolio.AvailablePercentage,
            ReservedPercentage = portfolio.ReservedPercentage,
            CreatedAt = portfolio.CreatedAt,
            LastUpdatedAt = portfolio.LastUpdatedAt
        };
    }
}

/// <summary>
/// Query handler for current portfolio statistics.
/// </summary>
public sealed class GetPortfolioStatisticsHandler : IQueryHandler<GetPortfolioStatisticsQuery, PortfolioStatistics>
{
    private readonly IPortfolioReadModel _portfolioReadModel;
    private readonly IPositionReadModel _positionReadModel;
    private readonly IExchangePort _exchangePort;

    public GetPortfolioStatisticsHandler(
        IPortfolioReadModel portfolioReadModel,
        IPositionReadModel positionReadModel,
        IExchangePort exchangePort)
    {
        _portfolioReadModel = portfolioReadModel ?? throw new ArgumentNullException(nameof(portfolioReadModel));
        _positionReadModel = positionReadModel ?? throw new ArgumentNullException(nameof(positionReadModel));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    public async Task<Result<PortfolioStatistics>> HandleAsync(
        GetPortfolioStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var from = query.From ?? DateTimeOffset.MinValue;
        var to = query.To ?? DateTimeOffset.UtcNow;
        if (from > to)
        {
            return Result<PortfolioStatistics>.Failure(
                Error.Validation("Portfolio statistics query range is invalid."));
        }

        var portfolio = query.PortfolioId is not null
            ? await _portfolioReadModel.GetByIdAsync(query.PortfolioId, cancellationToken)
            : await _portfolioReadModel.GetDefaultAsync(cancellationToken);

        if (portfolio is null)
        {
            var id = query.PortfolioId?.ToString() ?? "default";
            return Result<PortfolioStatistics>.Failure(Error.NotFound("Portfolio", id));
        }

        var activePositions = await _positionReadModel.GetActiveAsync(portfolio.Market, cancellationToken);
        var closedPositions = await _positionReadModel.GetClosedAsync(
            from,
            to,
            pair: null,
            limit: int.MaxValue,
            cancellationToken);
        var activeSnapshots = new List<PositionSnapshot>(activePositions.Count);

        foreach (var position in activePositions)
        {
            var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
            if (priceResult.IsFailure)
            {
                return Result<PortfolioStatistics>.Failure(priceResult.Error);
            }

            activeSnapshots.Add(PositionSnapshot.Create(position, priceResult.Value));
        }

        var currency = portfolio.Market;
        var totalUnrealizedPnL = SumMoney(activeSnapshots.Select(snapshot => snapshot.UnrealizedPnL), currency);
        var totalCurrentValue = SumMoney(activeSnapshots.Select(snapshot => snapshot.CurrentValue), currency);
        var totalCostBasis = activeSnapshots.Sum(snapshot => snapshot.CostBasis.Amount);
        var overallMargin = totalCostBasis == 0m
            ? Margin.Zero
            : Margin.Calculate(totalCostBasis, totalCurrentValue.Amount);
        var winningSnapshots = activeSnapshots.Where(snapshot => snapshot.Margin.IsProfit).ToList();
        var losingSnapshots = activeSnapshots.Where(snapshot => snapshot.Margin.IsLoss).ToList();
        var totalTrades = activePositions.Sum(position => position.EntryCount) + closedPositions.Count;
        var averageHoldingPeriod = CalculateAverageHoldingPeriod(activePositions, closedPositions);

        return Result<PortfolioStatistics>.Success(new PortfolioStatistics
        {
            PortfolioId = portfolio.Id,
            TotalBalance = portfolio.TotalBalance,
            AvailableBalance = portfolio.AvailableBalance,
            InvestedBalance = portfolio.InvestedBalance,
            ActivePositions = portfolio.ActivePositionCount,
            MaxPositions = portfolio.MaxPositions,
            PositionSlotsAvailable = Math.Max(0, portfolio.MaxPositions - portfolio.ActivePositionCount),
            TotalUnrealizedPnL = totalUnrealizedPnL,
            TotalRealizedPnL = Money.Zero(currency),
            OverallMargin = overallMargin,
            TotalTradesCount = totalTrades,
            WinningTradesCount = winningSnapshots.Count,
            LosingTradesCount = losingSnapshots.Count,
            WinRate = CalculateWinRate(winningSnapshots.Count, losingSnapshots.Count),
            AverageWin = AverageMoney(winningSnapshots.Select(snapshot => snapshot.UnrealizedPnL), currency),
            AverageLoss = AverageMoney(losingSnapshots.Select(snapshot => snapshot.UnrealizedPnL), currency),
            ProfitFactor = CalculateProfitFactor(winningSnapshots, losingSnapshots),
            MaxDrawdown = Money.Zero(currency),
            MaxDrawdownPercent = 0m,
            Sharpe = 0m,
            StatisticsAsOf = DateTimeOffset.UtcNow,
            AverageHoldingPeriod = averageHoldingPeriod
        });
    }

    private static decimal CalculateWinRate(int wins, int losses)
    {
        var completed = wins + losses;
        return completed == 0 ? 0m : (decimal)wins / completed * 100m;
    }

    private static decimal CalculateProfitFactor(
        IReadOnlyCollection<PositionSnapshot> winningSnapshots,
        IReadOnlyCollection<PositionSnapshot> losingSnapshots)
    {
        var wins = winningSnapshots.Sum(snapshot => snapshot.UnrealizedPnL.Amount);
        var losses = Math.Abs(losingSnapshots.Sum(snapshot => snapshot.UnrealizedPnL.Amount));
        return losses == 0m ? 0m : wins / losses;
    }

    private static TimeSpan CalculateAverageHoldingPeriod(
        IReadOnlyCollection<PositionReadModelEntry> activePositions,
        IReadOnlyCollection<PositionReadModelEntry> closedPositions)
    {
        var periods = activePositions
            .Select(position => DateTimeOffset.UtcNow - position.OpenedAt)
            .Concat(closedPositions.Select(position => (position.ClosedAt ?? DateTimeOffset.UtcNow) - position.OpenedAt))
            .ToList();

        if (periods.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)periods.Average(period => period.Ticks));
    }

    private static Money SumMoney(IEnumerable<Money> values, string currency)
    {
        return values.Aggregate(Money.Zero(currency), (sum, value) => sum + value);
    }

    private static Money AverageMoney(IEnumerable<Money> values, string currency)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return Money.Zero(currency);
        }

        return Money.Create(list.Average(value => value.Amount), currency);
    }

    private sealed record PositionSnapshot(
        Money CurrentValue,
        Money UnrealizedPnL,
        Money CostBasis,
        Margin Margin)
    {
        public static PositionSnapshot Create(PositionReadModelEntry position, Price currentPrice)
        {
            var currentValue = Money.Create(
                currentPrice.Value * position.TotalQuantity.Value,
                position.TotalCost.Currency);
            var costBasis = position.TotalCost + position.TotalFees;
            var unrealizedPnL = currentValue - costBasis;
            var margin = costBasis.Amount == 0m
                ? Margin.Zero
                : Margin.Calculate(costBasis.Amount, currentValue.Amount);

            return new PositionSnapshot(
                currentValue,
                unrealizedPnL,
                costBasis,
                margin);
        }
    }
}
