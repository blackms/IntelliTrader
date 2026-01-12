using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to execute a DCA (Dollar Cost Averaging) buy for an existing position.
/// </summary>
public sealed record ExecuteDCACommand
{
    /// <summary>
    /// The ID of the position to DCA into.
    /// </summary>
    public required PositionId PositionId { get; init; }

    /// <summary>
    /// The amount to invest in the DCA entry.
    /// </summary>
    public required Money Cost { get; init; }

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
/// Alternative command to execute DCA by trading pair.
/// </summary>
public sealed record ExecuteDCAByPairCommand
{
    /// <summary>
    /// The trading pair to DCA into.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The amount to invest in the DCA entry.
    /// </summary>
    public required Money Cost { get; init; }

    /// <summary>
    /// Optional maximum price to pay (for limit orders).
    /// </summary>
    public Price? MaxPrice { get; init; }

    /// <summary>
    /// Whether to use a limit order instead of market order.
    /// </summary>
    public bool UseLimitOrder { get; init; } = false;
}

/// <summary>
/// Result of executing a DCA entry.
/// </summary>
public sealed record ExecuteDCAResult
{
    public required PositionId PositionId { get; init; }
    public required TradingPair Pair { get; init; }
    public required OrderId OrderId { get; init; }
    public required Price EntryPrice { get; init; }
    public required Quantity Quantity { get; init; }
    public required Money Cost { get; init; }
    public required Money Fees { get; init; }
    public required int NewDCALevel { get; init; }
    public required Price NewAveragePrice { get; init; }
    public required Quantity NewTotalQuantity { get; init; }
    public required Money NewTotalCost { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
}
