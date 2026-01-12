using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Queries;

/// <summary>
/// Query to get a specific position.
/// </summary>
public sealed record GetPositionQuery
{
    public PositionId? PositionId { get; init; }
    public TradingPair? Pair { get; init; }
}

/// <summary>
/// Query to get all active positions.
/// </summary>
public sealed record GetActivePositionsQuery
{
    /// <summary>
    /// Optional filter by market (e.g., "USDT").
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Optional filter by minimum margin.
    /// </summary>
    public decimal? MinMargin { get; init; }

    /// <summary>
    /// Optional filter by maximum margin.
    /// </summary>
    public decimal? MaxMargin { get; init; }

    /// <summary>
    /// Optional sort order.
    /// </summary>
    public PositionSortOrder SortBy { get; init; } = PositionSortOrder.OpenedAt;

    /// <summary>
    /// Whether to sort descending.
    /// </summary>
    public bool Descending { get; init; } = true;
}

/// <summary>
/// Query to get closed positions.
/// </summary>
public sealed record GetClosedPositionsQuery
{
    /// <summary>
    /// Start date for the query range.
    /// </summary>
    public DateTimeOffset From { get; init; } = DateTimeOffset.UtcNow.AddDays(-30);

    /// <summary>
    /// End date for the query range.
    /// </summary>
    public DateTimeOffset To { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional filter by pair.
    /// </summary>
    public TradingPair? Pair { get; init; }

    /// <summary>
    /// Optional filter by profit only.
    /// </summary>
    public bool? ProfitableOnly { get; init; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; init; } = 100;
}

/// <summary>
/// Sort order for positions.
/// </summary>
public enum PositionSortOrder
{
    OpenedAt,
    Pair,
    Cost,
    Margin,
    DCALevel
}

/// <summary>
/// View model for position information.
/// </summary>
public sealed record PositionView
{
    public required PositionId Id { get; init; }
    public required TradingPair Pair { get; init; }
    public required Price AveragePrice { get; init; }
    public required Price CurrentPrice { get; init; }
    public required Quantity TotalQuantity { get; init; }
    public required Money TotalCost { get; init; }
    public required Money TotalFees { get; init; }
    public required Money CurrentValue { get; init; }
    public required Money UnrealizedPnL { get; init; }
    public required Margin CurrentMargin { get; init; }
    public required int DCALevel { get; init; }
    public required int EntryCount { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
    public required TimeSpan HoldingPeriod { get; init; }
    public string? SignalRule { get; init; }
    public bool IsClosed { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public Money? RealizedPnL { get; init; }
}

/// <summary>
/// Summary of all positions.
/// </summary>
public sealed record PositionsSummary
{
    public required int TotalPositions { get; init; }
    public required int ProfitablePositions { get; init; }
    public required int LosingPositions { get; init; }
    public required Money TotalInvested { get; init; }
    public required Money TotalCurrentValue { get; init; }
    public required Money TotalUnrealizedPnL { get; init; }
    public required Margin AverageMargin { get; init; }
    public required Margin BestMargin { get; init; }
    public required Margin WorstMargin { get; init; }
    public TradingPair? BestPerformer { get; init; }
    public TradingPair? WorstPerformer { get; init; }
}
