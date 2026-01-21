namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to place a sell order using the legacy trading service.
/// This command bridges the Application layer to the legacy ITradingService for gradual migration.
/// </summary>
public sealed record PlaceSellOrderCommand
{
    /// <summary>
    /// The trading pair to sell (e.g., "BTCUSDT").
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// Optional amount to sell. If not specified, the entire position is sold.
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Whether this is a manual order (triggered by user) vs automatic (triggered by rules).
    /// </summary>
    public bool IsManualOrder { get; init; } = false;

    /// <summary>
    /// Optional swap pair if this sell is part of a swap operation.
    /// </summary>
    public string? SwapPair { get; init; }

    /// <summary>
    /// Optional metadata for tracking purposes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of placing a sell order.
/// </summary>
public sealed record PlaceSellOrderResult
{
    /// <summary>
    /// Whether the order was successfully placed.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The trading pair that was sold.
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
    /// The quantity that was sold.
    /// </summary>
    public decimal? Quantity { get; init; }

    /// <summary>
    /// The total proceeds from the sale.
    /// </summary>
    public decimal? Proceeds { get; init; }

    /// <summary>
    /// The fees paid for the order.
    /// </summary>
    public decimal? Fees { get; init; }

    /// <summary>
    /// The realized profit/loss from the sale.
    /// </summary>
    public decimal? RealizedPnL { get; init; }

    /// <summary>
    /// Timestamp when the order was placed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Error message if the order failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
