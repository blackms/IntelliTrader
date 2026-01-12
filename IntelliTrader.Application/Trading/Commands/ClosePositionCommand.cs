using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to close an existing trading position.
/// </summary>
public sealed record ClosePositionCommand
{
    /// <summary>
    /// The ID of the position to close.
    /// </summary>
    public required PositionId PositionId { get; init; }

    /// <summary>
    /// Optional minimum price to sell at (for limit orders).
    /// If not specified, a market order will be placed.
    /// </summary>
    public Price? MinPrice { get; init; }

    /// <summary>
    /// Whether to use a limit order instead of market order.
    /// </summary>
    public bool UseLimitOrder { get; init; } = false;

    /// <summary>
    /// The reason for closing the position.
    /// </summary>
    public CloseReason Reason { get; init; } = CloseReason.Manual;

    /// <summary>
    /// Optional metadata for tracking purposes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Alternative command to close a position by trading pair.
/// </summary>
public sealed record ClosePositionByPairCommand
{
    /// <summary>
    /// The trading pair to close the position for.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// Optional minimum price to sell at (for limit orders).
    /// </summary>
    public Price? MinPrice { get; init; }

    /// <summary>
    /// Whether to use a limit order instead of market order.
    /// </summary>
    public bool UseLimitOrder { get; init; } = false;

    /// <summary>
    /// The reason for closing the position.
    /// </summary>
    public CloseReason Reason { get; init; } = CloseReason.Manual;
}

/// <summary>
/// Reason for closing a position.
/// </summary>
public enum CloseReason
{
    Manual,
    TakeProfit,
    StopLoss,
    TrailingStop,
    SignalRule,
    MarginCall,
    SystemShutdown
}

/// <summary>
/// Result of closing a position.
/// </summary>
public sealed record ClosePositionResult
{
    public required PositionId PositionId { get; init; }
    public required TradingPair Pair { get; init; }
    public required OrderId SellOrderId { get; init; }
    public required Price SellPrice { get; init; }
    public required Quantity Quantity { get; init; }
    public required Money Proceeds { get; init; }
    public required Money Fees { get; init; }
    public required Money TotalCost { get; init; }
    public required Money RealizedPnL { get; init; }
    public required Margin RealizedMargin { get; init; }
    public required TimeSpan HoldingPeriod { get; init; }
    public required CloseReason Reason { get; init; }
    public required DateTimeOffset ClosedAt { get; init; }
}
