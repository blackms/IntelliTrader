using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading;

/// <summary>
/// Application service implementing the primary trading port.
/// Primary adapters should depend on this use case instead of individual dispatchers.
/// </summary>
public sealed class TradingUseCase : ITradingUseCase
{
    private const int UnboundedDcaLevels = int.MaxValue;

    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangePort _exchangePort;

    public TradingUseCase(
        ICommandDispatcher commandDispatcher,
        IQueryDispatcher queryDispatcher,
        IPortfolioRepository portfolioRepository,
        IPositionRepository positionRepository,
        IExchangePort exchangePort)
    {
        _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
        _queryDispatcher = queryDispatcher ?? throw new ArgumentNullException(nameof(queryDispatcher));
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    public Task<Result<OpenPositionResult>> OpenPositionAsync(
        OpenPositionCommand command,
        CancellationToken cancellationToken = default)
    {
        return _commandDispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(
            command,
            cancellationToken);
    }

    public Task<Result<ClosePositionResult>> ClosePositionAsync(
        ClosePositionCommand command,
        CancellationToken cancellationToken = default)
    {
        return _commandDispatcher.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
            command,
            cancellationToken);
    }

    public Task<Result<ClosePositionResult>> ClosePositionByPairAsync(
        ClosePositionByPairCommand command,
        CancellationToken cancellationToken = default)
    {
        return _commandDispatcher.DispatchAsync<ClosePositionByPairCommand, ClosePositionResult>(
            command,
            cancellationToken);
    }

    public Task<Result<ExecuteDCAResult>> ExecuteDCAAsync(
        ExecuteDCACommand command,
        CancellationToken cancellationToken = default)
    {
        return _commandDispatcher.DispatchAsync<ExecuteDCACommand, ExecuteDCAResult>(
            command,
            cancellationToken);
    }

    public Task<Result<ExecuteDCAResult>> ExecuteDCAByPairAsync(
        ExecuteDCAByPairCommand command,
        CancellationToken cancellationToken = default)
    {
        return _commandDispatcher.DispatchAsync<ExecuteDCAByPairCommand, ExecuteDCAResult>(
            command,
            cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ClosePositionResult>>> CloseAllPositionsAsync(
        CloseReason reason = CloseReason.Manual,
        CancellationToken cancellationToken = default)
    {
        var positions = await _positionRepository.GetAllActiveAsync(cancellationToken);
        var results = new List<ClosePositionResult>(positions.Count);

        foreach (var position in positions)
        {
            var result = await ClosePositionAsync(
                new ClosePositionCommand
                {
                    PositionId = position.Id,
                    Reason = reason
                },
                cancellationToken);

            if (result.IsFailure)
            {
                return Result<IReadOnlyList<ClosePositionResult>>.Failure(result.Error);
            }

            results.Add(result.Value);
        }

        return Result<IReadOnlyList<ClosePositionResult>>.Success(results);
    }

    public Task<Result<PositionView>> GetPositionAsync(
        GetPositionQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetPositionQuery, PositionView>(query, cancellationToken);
    }

    public Task<Result<IReadOnlyList<PositionView>>> GetActivePositionsAsync(
        GetActivePositionsQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetActivePositionsQuery, IReadOnlyList<PositionView>>(
            query,
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<PositionView>>> GetClosedPositionsAsync(
        GetClosedPositionsQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetClosedPositionsQuery, IReadOnlyList<PositionView>>(
            query,
            cancellationToken);
    }

    public async Task<Result<PositionsSummary>> GetPositionsSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var positionsResult = await GetActivePositionsAsync(new GetActivePositionsQuery(), cancellationToken);
        if (positionsResult.IsFailure)
        {
            return Result<PositionsSummary>.Failure(positionsResult.Error);
        }

        var positions = positionsResult.Value;
        var currency = ResolveSummaryCurrency(positions);

        if (positions.Count == 0)
        {
            return Result<PositionsSummary>.Success(CreateEmptySummary(currency));
        }

        var totalInvested = SumMoney(positions.Select(position => position.TotalCost), currency);
        var totalCurrentValue = SumMoney(positions.Select(position => position.CurrentValue), currency);
        var totalUnrealizedPnL = SumMoney(positions.Select(position => position.UnrealizedPnL), currency);
        var averageMargin = Margin.FromPercentage(positions.Average(position => position.CurrentMargin.Percentage));
        var best = positions.MaxBy(position => position.CurrentMargin.Percentage);
        var worst = positions.MinBy(position => position.CurrentMargin.Percentage);

        return Result<PositionsSummary>.Success(new PositionsSummary
        {
            TotalPositions = positions.Count,
            ProfitablePositions = positions.Count(position => position.CurrentMargin.IsProfit),
            LosingPositions = positions.Count(position => position.CurrentMargin.IsLoss),
            TotalInvested = totalInvested,
            TotalCurrentValue = totalCurrentValue,
            TotalUnrealizedPnL = totalUnrealizedPnL,
            AverageMargin = averageMargin,
            BestMargin = best?.CurrentMargin ?? Margin.Zero,
            WorstMargin = worst?.CurrentMargin ?? Margin.Zero,
            BestPerformer = best?.Pair,
            WorstPerformer = worst?.Pair
        });
    }

    public Task<Result<PortfolioView>> GetPortfolioAsync(
        GetPortfolioQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetPortfolioQuery, PortfolioView>(query, cancellationToken);
    }

    public Task<Result<PortfolioStatistics>> GetPortfolioStatisticsAsync(
        GetPortfolioStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetPortfolioStatisticsQuery, PortfolioStatistics>(
            query,
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<TradeHistoryEntry>>> GetTradingHistoryAsync(
        GetTradingHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queryDispatcher.DispatchAsync<GetTradingHistoryQuery, IReadOnlyList<TradeHistoryEntry>>(
            query,
            cancellationToken);
    }

    public async Task<Result<CanOpenPositionResult>> CanOpenPositionAsync(
        TradingPair pair,
        Money cost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(cost);

        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<CanOpenPositionResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        var validationErrors = new List<string>();
        if (!cost.Currency.Equals(portfolio.Market, StringComparison.Ordinal))
        {
            validationErrors.Add($"Cost currency {cost.Currency} does not match portfolio market {portfolio.Market}.");
        }
        else
        {
            if (cost < portfolio.MinPositionCost)
            {
                validationErrors.Add($"Requested cost {cost} is below minimum position cost {portfolio.MinPositionCost}.");
            }

            if (!portfolio.CanAfford(cost))
            {
                validationErrors.Add($"Insufficient funds. Available: {portfolio.Balance.Available}, Required: {cost}.");
            }
        }

        if (portfolio.HasPositionFor(pair))
        {
            validationErrors.Add($"Position already exists for {pair.Symbol}.");
        }

        if (portfolio.IsAtMaxPositions)
        {
            validationErrors.Add($"Maximum positions ({portfolio.MaxPositions}) reached.");
        }

        return Result<CanOpenPositionResult>.Success(new CanOpenPositionResult
        {
            CanOpen = validationErrors.Count == 0,
            Pair = pair,
            RequestedCost = cost,
            AvailableBalance = portfolio.Balance.Available,
            CurrentPositionCount = portfolio.ActivePositionCount,
            MaxPositions = portfolio.MaxPositions,
            ValidationErrors = validationErrors
        });
    }

    public async Task<Result<CanDCAResult>> CanExecuteDCAAsync(
        PositionId positionId,
        Money cost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(cost);

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            return Result<CanDCAResult>.Failure(Error.NotFound("Position", positionId.ToString()));
        }

        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<CanDCAResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
        if (priceResult.IsFailure)
        {
            return Result<CanDCAResult>.Failure(priceResult.Error);
        }

        var validationErrors = new List<string>();
        if (position.IsClosed)
        {
            validationErrors.Add($"Position {positionId} is closed.");
        }

        if (!portfolio.HasPositionFor(position.Pair))
        {
            validationErrors.Add($"Portfolio does not contain active position for {position.Pair.Symbol}.");
        }

        if (!cost.Currency.Equals(portfolio.Market, StringComparison.Ordinal))
        {
            validationErrors.Add($"Cost currency {cost.Currency} does not match portfolio market {portfolio.Market}.");
        }
        else if (!portfolio.CanAfford(cost))
        {
            validationErrors.Add($"Insufficient funds. Available: {portfolio.Balance.Available}, Required: {cost}.");
        }

        return Result<CanDCAResult>.Success(new CanDCAResult
        {
            CanDCA = validationErrors.Count == 0,
            PositionId = position.Id,
            Pair = position.Pair,
            CurrentDCALevel = position.DCALevel,
            MaxDCALevels = UnboundedDcaLevels,
            RequestedCost = cost,
            AvailableBalance = portfolio.Balance.Available,
            CurrentMargin = position.CalculateMargin(priceResult.Value),
            ValidationErrors = validationErrors
        });
    }

    private static string ResolveSummaryCurrency(IReadOnlyList<PositionView> positions)
    {
        return positions.FirstOrDefault()?.TotalCost.Currency ?? "USDT";
    }

    private static PositionsSummary CreateEmptySummary(string currency)
    {
        return new PositionsSummary
        {
            TotalPositions = 0,
            ProfitablePositions = 0,
            LosingPositions = 0,
            TotalInvested = Money.Zero(currency),
            TotalCurrentValue = Money.Zero(currency),
            TotalUnrealizedPnL = Money.Zero(currency),
            AverageMargin = Margin.Zero,
            BestMargin = Margin.Zero,
            WorstMargin = Margin.Zero
        };
    }

    private static Money SumMoney(IEnumerable<Money> values, string currency)
    {
        return values.Aggregate(Money.Zero(currency), (sum, value) => sum + value);
    }
}
