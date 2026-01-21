namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to place a buy order using the legacy trading service.
/// This command bridges the Application layer to the legacy ITradingService for gradual migration.
/// </summary>
public sealed record PlaceBuyOrderCommand
{
    /// <summary>
    /// The trading pair to buy (e.g., "BTCUSDT").
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// Optional amount to buy. If not specified, the default from config is used.
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Optional maximum cost for the order.
    /// </summary>
    public decimal? MaxCost { get; init; }

    /// <summary>
    /// Whether to ignore existing positions for this pair.
    /// </summary>
    public bool IgnoreExisting { get; init; } = false;

    /// <summary>
    /// Whether this is a manual order (triggered by user) vs automatic (triggered by rules).
    /// </summary>
    public bool IsManualOrder { get; init; } = false;

    /// <summary>
    /// Optional metadata for tracking purposes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of placing a buy order.
/// </summary>
public sealed record PlaceBuyOrderResult
{
    /// <summary>
    /// Whether the order was successfully placed.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The trading pair that was bought.
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// The order ID from the exchange, if available.
    /// </summary>
    public long? OrderId { get; init; }

    /// <summary>
    /// The price at which the order was filled.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// The quantity that was purchased.
    /// </summary>
    public decimal? Quantity { get; init; }

    /// <summary>
    /// The total cost of the order.
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>
    /// The fees paid for the order.
    /// </summary>
    public decimal? Fees { get; init; }

    /// <summary>
    /// Timestamp when the order was placed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Error message if the order failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
