using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to open a new trading position.
/// </summary>
public sealed record OpenPositionCommand
{
    /// <summary>
    /// The trading pair to open a position for.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The amount to invest in the position.
    /// </summary>
    public required Money Cost { get; init; }

    /// <summary>
    /// Optional signal rule that triggered this position.
    /// </summary>
    public string? SignalRule { get; init; }

    /// <summary>
    /// Optional maximum price to pay (for limit orders).
    /// If not specified, a market order will be placed.
    /// </summary>
    public Price? MaxPrice { get; init; }

    /// <summary>
    /// Whether to use a limit order instead of market order.
    /// </summary>
    public bool UseLimitOrder { get; init; } = false;

    /// <summary>
    /// Optional metadata for tracking purposes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of opening a position.
/// </summary>
public sealed record OpenPositionResult
{
    public required PositionId PositionId { get; init; }
    public required TradingPair Pair { get; init; }
    public required OrderId OrderId { get; init; }
    public required Price EntryPrice { get; init; }
    public required Quantity Quantity { get; init; }
    public required Money Cost { get; init; }
    public required Money Fees { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
}
