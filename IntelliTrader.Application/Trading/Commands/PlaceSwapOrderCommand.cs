namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to place a swap order (sell one pair and buy another) using the legacy trading service.
/// This command bridges the Application layer to the legacy ITradingService for gradual migration.
/// </summary>
public sealed record PlaceSwapOrderCommand
{
    /// <summary>
    /// The trading pair to sell (e.g., "BTCUSDT").
    /// </summary>
    public required string OldPair { get; init; }

    /// <summary>
    /// The trading pair to buy with the proceeds (e.g., "ETHUSDT").
    /// </summary>
    public required string NewPair { get; init; }

    /// <summary>
    /// Whether this is a manual order (triggered by user) vs automatic (triggered by rules).
    /// </summary>
    public bool IsManualOrder { get; init; } = false;

    /// <summary>
    /// Optional metadata for tracking the new pair position.
    /// </summary>
    public IReadOnlyDictionary<string, string>? NewPairMetadata { get; init; }
}

/// <summary>
/// Result of placing a swap order.
/// </summary>
public sealed record PlaceSwapOrderResult
{
    /// <summary>
    /// Whether the swap was successfully executed.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The trading pair that was sold.
    /// </summary>
    public required string OldPair { get; init; }

    /// <summary>
    /// The trading pair that was bought.
    /// </summary>
    public required string NewPair { get; init; }

    /// <summary>
    /// Result details of the sell order.
    /// </summary>
    public PlaceSellOrderResult? SellResult { get; init; }

    /// <summary>
    /// Result details of the buy order.
    /// </summary>
    public PlaceBuyOrderResult? BuyResult { get; init; }

    /// <summary>
    /// Timestamp when the swap was initiated.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Error message if the swap failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
