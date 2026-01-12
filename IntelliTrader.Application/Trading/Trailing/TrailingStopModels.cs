using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Trailing;

/// <summary>
/// Action to take when trailing stop conditions are met.
/// </summary>
public enum TrailingStopAction
{
    /// <summary>
    /// Execute the trade (buy or sell).
    /// </summary>
    Execute,

    /// <summary>
    /// Cancel the trailing without executing.
    /// </summary>
    Cancel
}

/// <summary>
/// Type of trailing stop.
/// </summary>
public enum TrailingType
{
    /// <summary>
    /// Trailing buy - follows price down, triggers buy when price rises.
    /// </summary>
    Buy,

    /// <summary>
    /// Trailing sell - follows price up, triggers sell when price drops.
    /// </summary>
    Sell
}

/// <summary>
/// Result of a trailing stop check.
/// </summary>
public enum TrailingCheckResult
{
    /// <summary>
    /// Continue trailing.
    /// </summary>
    Continue,

    /// <summary>
    /// Trailing triggered - execute the trade.
    /// </summary>
    Triggered,

    /// <summary>
    /// Trailing cancelled due to stop margin.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Trailing cancelled because trading is disabled.
    /// </summary>
    Disabled
}

/// <summary>
/// Configuration for trailing stops.
/// </summary>
public sealed record TrailingConfig
{
    /// <summary>
    /// The trailing percentage. For sells, triggers when margin drops this much from best.
    /// For buys, triggers when margin rises this much from best.
    /// </summary>
    public required decimal TrailingPercentage { get; init; }

    /// <summary>
    /// The stop margin threshold. For sells, cancels if margin drops below this.
    /// For buys, cancels if margin rises above this.
    /// </summary>
    public required decimal StopMargin { get; init; }

    /// <summary>
    /// Action to take when stop margin is reached.
    /// </summary>
    public TrailingStopAction StopAction { get; init; } = TrailingStopAction.Execute;
}

/// <summary>
/// State of an active trailing stop for a sell order.
/// </summary>
public sealed class SellTrailingState
{
    /// <summary>
    /// The position ID being trailed.
    /// </summary>
    public required PositionId PositionId { get; init; }

    /// <summary>
    /// The trading pair.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The trailing configuration.
    /// </summary>
    public required TrailingConfig Config { get; init; }

    /// <summary>
    /// The target margin that triggered this trailing (for logging purposes).
    /// </summary>
    public required decimal TargetMargin { get; init; }

    /// <summary>
    /// The price when trailing was initiated.
    /// </summary>
    public required Price InitialPrice { get; init; }

    /// <summary>
    /// The margin when trailing was initiated.
    /// </summary>
    public required decimal InitialMargin { get; init; }

    /// <summary>
    /// The best (highest) margin seen during trailing.
    /// </summary>
    public decimal BestMargin { get; set; }

    /// <summary>
    /// The last margin recorded.
    /// </summary>
    public decimal LastMargin { get; set; }

    /// <summary>
    /// When trailing was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// State of an active trailing stop for a buy order (DCA).
/// </summary>
public sealed class BuyTrailingState
{
    /// <summary>
    /// The position ID being trailed (if DCA) or null for new positions.
    /// </summary>
    public PositionId? PositionId { get; init; }

    /// <summary>
    /// The trading pair.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The trailing configuration.
    /// </summary>
    public required TrailingConfig Config { get; init; }

    /// <summary>
    /// The cost to use for the buy order.
    /// </summary>
    public required Money Cost { get; init; }

    /// <summary>
    /// The price when trailing was initiated.
    /// </summary>
    public required Price InitialPrice { get; init; }

    /// <summary>
    /// The margin when trailing was initiated (relative to initial price).
    /// </summary>
    public required decimal InitialMargin { get; init; }

    /// <summary>
    /// The best (lowest) margin seen during trailing.
    /// </summary>
    public decimal BestMargin { get; set; }

    /// <summary>
    /// The last margin recorded.
    /// </summary>
    public decimal LastMargin { get; set; }

    /// <summary>
    /// The signal rule that triggered this buy (if any).
    /// </summary>
    public string? SignalRule { get; init; }

    /// <summary>
    /// When trailing was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of processing a trailing update.
/// </summary>
public sealed record TrailingUpdateResult
{
    /// <summary>
    /// The type of trailing.
    /// </summary>
    public required TrailingType Type { get; init; }

    /// <summary>
    /// The trading pair.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The position ID (if applicable).
    /// </summary>
    public PositionId? PositionId { get; init; }

    /// <summary>
    /// The result of the check.
    /// </summary>
    public required TrailingCheckResult Result { get; init; }

    /// <summary>
    /// The current price.
    /// </summary>
    public required Price CurrentPrice { get; init; }

    /// <summary>
    /// The current margin.
    /// </summary>
    public required decimal CurrentMargin { get; init; }

    /// <summary>
    /// The best margin achieved during trailing.
    /// </summary>
    public required decimal BestMargin { get; init; }

    /// <summary>
    /// The trailing configuration.
    /// </summary>
    public required TrailingConfig Config { get; init; }

    /// <summary>
    /// The cost for buy orders (if applicable).
    /// </summary>
    public Money? Cost { get; init; }

    /// <summary>
    /// The signal rule (if applicable).
    /// </summary>
    public string? SignalRule { get; init; }

    /// <summary>
    /// Reason for the result.
    /// </summary>
    public string? Reason { get; init; }
}
