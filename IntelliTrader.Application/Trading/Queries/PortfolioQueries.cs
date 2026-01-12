using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Queries;

/// <summary>
/// Query to get portfolio information.
/// </summary>
public sealed record GetPortfolioQuery
{
    public PortfolioId? PortfolioId { get; init; }
    public string? Name { get; init; }
}

/// <summary>
/// View model for portfolio information.
/// </summary>
public sealed record PortfolioView
{
    public required PortfolioId Id { get; init; }
    public required string Name { get; init; }
    public required string Market { get; init; }
    public required Money TotalBalance { get; init; }
    public required Money AvailableBalance { get; init; }
    public required Money ReservedBalance { get; init; }
    public required int ActivePositionCount { get; init; }
    public required int MaxPositions { get; init; }
    public required Money MinPositionCost { get; init; }
    public required bool CanOpenNewPosition { get; init; }
    public required decimal AvailablePercentage { get; init; }
    public required decimal ReservedPercentage { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
}

/// <summary>
/// Detailed portfolio statistics.
/// </summary>
public sealed record PortfolioStatistics
{
    public required PortfolioId PortfolioId { get; init; }

    // Balance Info
    public required Money TotalBalance { get; init; }
    public required Money AvailableBalance { get; init; }
    public required Money InvestedBalance { get; init; }

    // Position Stats
    public required int ActivePositions { get; init; }
    public required int MaxPositions { get; init; }
    public required int PositionSlotsAvailable { get; init; }

    // P&L Stats
    public required Money TotalUnrealizedPnL { get; init; }
    public required Money TotalRealizedPnL { get; init; }
    public required Margin OverallMargin { get; init; }

    // Performance Metrics
    public required int TotalTradesCount { get; init; }
    public required int WinningTradesCount { get; init; }
    public required int LosingTradesCount { get; init; }
    public required decimal WinRate { get; init; }
    public required Money AverageWin { get; init; }
    public required Money AverageLoss { get; init; }
    public required decimal ProfitFactor { get; init; }

    // Risk Metrics
    public required Money MaxDrawdown { get; init; }
    public required decimal MaxDrawdownPercent { get; init; }
    public required decimal Sharpe { get; init; }

    // Time-based
    public required DateTimeOffset StatisticsAsOf { get; init; }
    public required TimeSpan AverageHoldingPeriod { get; init; }
}

/// <summary>
/// Query to get portfolio statistics.
/// </summary>
public sealed record GetPortfolioStatisticsQuery
{
    public PortfolioId? PortfolioId { get; init; }

    /// <summary>
    /// Start date for historical statistics.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// End date for historical statistics.
    /// </summary>
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// Query to get trading history.
/// </summary>
public sealed record GetTradingHistoryQuery
{
    public PortfolioId? PortfolioId { get; init; }
    public DateTimeOffset From { get; init; } = DateTimeOffset.UtcNow.AddDays(-30);
    public DateTimeOffset To { get; init; } = DateTimeOffset.UtcNow;
    public TradingPair? Pair { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; } = 0;
}

/// <summary>
/// Single trade history entry.
/// </summary>
public sealed record TradeHistoryEntry
{
    public required PositionId PositionId { get; init; }
    public required TradingPair Pair { get; init; }
    public required TradeType Type { get; init; }
    public required Price Price { get; init; }
    public required Quantity Quantity { get; init; }
    public required Money Cost { get; init; }
    public required Money Fees { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? OrderId { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// Type of trade.
/// </summary>
public enum TradeType
{
    Buy,
    Sell,
    DCA
}
