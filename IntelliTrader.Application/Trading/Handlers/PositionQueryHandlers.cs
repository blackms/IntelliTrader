using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Query handler for a single trading position.
/// </summary>
public sealed class GetPositionHandler : IQueryHandler<GetPositionQuery, PositionView>
{
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangePort _exchangePort;

    public GetPositionHandler(IPositionRepository positionRepository, IExchangePort exchangePort)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    public async Task<Result<PositionView>> HandleAsync(
        GetPositionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PositionId is null && query.Pair is null)
        {
            return Result<PositionView>.Failure(
                Error.Validation("PositionId or Pair is required."));
        }

        var position = query.PositionId is not null
            ? await _positionRepository.GetByIdAsync(query.PositionId, cancellationToken)
            : await _positionRepository.GetByPairAsync(query.Pair!, cancellationToken);

        if (position is null)
        {
            var id = query.PositionId?.ToString() ?? query.Pair!.Symbol;
            return Result<PositionView>.Failure(Error.NotFound("Position", id));
        }

        var priceResult = await ResolveCurrentPriceAsync(position, cancellationToken);
        if (priceResult.IsFailure)
        {
            return Result<PositionView>.Failure(priceResult.Error);
        }

        return Result<PositionView>.Success(PositionQueryMapper.Map(position, priceResult.Value));
    }

    private async Task<Result<Price>> ResolveCurrentPriceAsync(
        Position position,
        CancellationToken cancellationToken)
    {
        if (position.IsClosed)
        {
            return Result<Price>.Success(position.AveragePrice);
        }

        return await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
    }
}

/// <summary>
/// Query handler for active trading positions.
/// </summary>
public sealed class GetActivePositionsHandler : IQueryHandler<GetActivePositionsQuery, IReadOnlyList<PositionView>>
{
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangePort _exchangePort;

    public GetActivePositionsHandler(IPositionRepository positionRepository, IExchangePort exchangePort)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    public async Task<Result<IReadOnlyList<PositionView>>> HandleAsync(
        GetActivePositionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var positions = await _positionRepository.GetAllActiveAsync(cancellationToken);
        var filteredPositions = positions
            .Where(position => query.Market is null || position.Pair.IsInMarket(query.Market))
            .ToList();

        var views = new List<PositionView>(filteredPositions.Count);
        foreach (var position in filteredPositions)
        {
            var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
            if (priceResult.IsFailure)
            {
                return Result<IReadOnlyList<PositionView>>.Failure(priceResult.Error);
            }

            views.Add(PositionQueryMapper.Map(position, priceResult.Value));
        }

        views = Sort(views, query.SortBy, query.Descending).ToList();

        if (query.MinMargin.HasValue)
        {
            views = views
                .Where(position => position.CurrentMargin.Percentage >= query.MinMargin.Value)
                .ToList();
        }

        if (query.MaxMargin.HasValue)
        {
            views = views
                .Where(position => position.CurrentMargin.Percentage <= query.MaxMargin.Value)
                .ToList();
        }

        return Result<IReadOnlyList<PositionView>>.Success(views);
    }

    private static IEnumerable<PositionView> Sort(
        IEnumerable<PositionView> positions,
        PositionSortOrder sortBy,
        bool descending)
    {
        return (sortBy, descending) switch
        {
            (PositionSortOrder.Pair, true) => positions.OrderByDescending(position => position.Pair.Symbol),
            (PositionSortOrder.Pair, false) => positions.OrderBy(position => position.Pair.Symbol),
            (PositionSortOrder.Cost, true) => positions.OrderByDescending(position => position.TotalCost.Amount),
            (PositionSortOrder.Cost, false) => positions.OrderBy(position => position.TotalCost.Amount),
            (PositionSortOrder.Margin, true) => positions.OrderByDescending(position => position.CurrentMargin.Percentage),
            (PositionSortOrder.Margin, false) => positions.OrderBy(position => position.CurrentMargin.Percentage),
            (PositionSortOrder.DCALevel, true) => positions.OrderByDescending(position => position.DCALevel),
            (PositionSortOrder.DCALevel, false) => positions.OrderBy(position => position.DCALevel),
            (_, true) => positions.OrderByDescending(position => position.OpenedAt),
            (_, false) => positions.OrderBy(position => position.OpenedAt)
        };
    }
}

/// <summary>
/// Query handler for closed trading positions.
/// </summary>
public sealed class GetClosedPositionsHandler : IQueryHandler<GetClosedPositionsQuery, IReadOnlyList<PositionView>>
{
    private readonly IPositionRepository _positionRepository;

    public GetClosedPositionsHandler(IPositionRepository positionRepository)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
    }

    public async Task<Result<IReadOnlyList<PositionView>>> HandleAsync(
        GetClosedPositionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.From > query.To)
        {
            return Result<IReadOnlyList<PositionView>>.Failure(
                Error.Validation("Closed positions query range is invalid."));
        }

        if (query.ProfitableOnly.HasValue)
        {
            return Result<IReadOnlyList<PositionView>>.Failure(
                Error.Validation("ProfitableOnly requires a closed-position PnL projection."));
        }

        var limit = query.Limit <= 0 ? 100 : query.Limit;
        var positions = await _positionRepository.GetClosedPositionsAsync(query.From, query.To, cancellationToken);

        var views = positions
            .Where(position => position.IsClosed)
            .Where(position => query.Pair is null || position.Pair.Equals(query.Pair))
            .OrderByDescending(position => position.ClosedAt ?? position.OpenedAt)
            .Take(limit)
            .Select(position => PositionQueryMapper.Map(position, position.AveragePrice))
            .ToList();

        return Result<IReadOnlyList<PositionView>>.Success(views);
    }
}

internal static class PositionQueryMapper
{
    public static PositionView Map(Position position, Price currentPrice)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(currentPrice);

        return new PositionView
        {
            Id = position.Id,
            Pair = position.Pair,
            AveragePrice = position.AveragePrice,
            CurrentPrice = currentPrice,
            TotalQuantity = position.TotalQuantity,
            TotalCost = position.TotalCost,
            TotalFees = position.TotalFees,
            CurrentValue = position.CalculateCurrentValue(currentPrice),
            UnrealizedPnL = position.CalculateUnrealizedPnL(currentPrice),
            CurrentMargin = position.CalculateMargin(currentPrice),
            DCALevel = position.DCALevel,
            EntryCount = position.Entries.Count,
            OpenedAt = position.OpenedAt,
            HoldingPeriod = (position.ClosedAt ?? DateTimeOffset.UtcNow) - position.OpenedAt,
            SignalRule = position.SignalRule,
            IsClosed = position.IsClosed,
            ClosedAt = position.ClosedAt,
            RealizedPnL = null
        };
    }
}
