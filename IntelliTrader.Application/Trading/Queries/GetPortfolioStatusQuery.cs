namespace IntelliTrader.Application.Trading.Queries;

/// <summary>
/// Query to get the current portfolio status from the legacy trading service.
/// This bridges the Application layer to the legacy service for read operations.
/// </summary>
public sealed record GetPortfolioStatusQuery
{
    /// <summary>
    /// Whether to include detailed position information.
    /// </summary>
    public bool IncludePositions { get; init; } = true;

    /// <summary>
    /// Whether to include trailing order information.
    /// </summary>
    public bool IncludeTrailingOrders { get; init; } = true;

    /// <summary>
    /// Whether to include order history.
    /// </summary>
    public bool IncludeOrderHistory { get; init; } = false;

    /// <summary>
    /// Maximum number of order history entries to include.
    /// </summary>
    public int OrderHistoryLimit { get; init; } = 50;
}

/// <summary>
/// Result of the portfolio status query.
/// </summary>
public sealed record GetPortfolioStatusResult
{
    /// <summary>
    /// Summary information about the portfolio.
    /// </summary>
    public required PortfolioSummary Summary { get; init; }

    /// <summary>
    /// List of active positions, if requested.
    /// </summary>
    public IReadOnlyList<PositionInfo>? Positions { get; init; }

    /// <summary>
    /// List of pairs with trailing buy orders, if requested.
    /// </summary>
    public IReadOnlyList<string>? TrailingBuys { get; init; }

    /// <summary>
    /// List of pairs with trailing sell orders, if requested.
    /// </summary>
    public IReadOnlyList<string>? TrailingSells { get; init; }

    /// <summary>
    /// Recent order history, if requested.
    /// </summary>
    public IReadOnlyList<OrderHistoryInfo>? OrderHistory { get; init; }

    /// <summary>
    /// Timestamp when the status was retrieved.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Summary information about the portfolio.
/// </summary>
public sealed record PortfolioSummary
{
    /// <summary>
    /// Total balance in the trading account.
    /// </summary>
    public required decimal TotalBalance { get; init; }

    /// <summary>
    /// Available balance for new orders.
    /// </summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>
    /// Balance reserved in open positions.
    /// </summary>
    public required decimal ReservedBalance { get; init; }

    /// <summary>
    /// Current value of all positions at market prices.
    /// </summary>
    public required decimal CurrentValue { get; init; }

    /// <summary>
    /// Total unrealized profit/loss.
    /// </summary>
    public required decimal UnrealizedPnL { get; init; }

    /// <summary>
    /// Unrealized profit/loss as a percentage.
    /// </summary>
    public required decimal UnrealizedPnLPercent { get; init; }

    /// <summary>
    /// Number of active positions.
    /// </summary>
    public required int ActivePositionCount { get; init; }

    /// <summary>
    /// Number of pairs with trailing buy orders.
    /// </summary>
    public required int TrailingBuyCount { get; init; }

    /// <summary>
    /// Number of pairs with trailing sell orders.
    /// </summary>
    public required int TrailingSellCount { get; init; }

    /// <summary>
    /// Whether trading is currently suspended.
    /// </summary>
    public required bool IsTradingSuspended { get; init; }

    /// <summary>
    /// The configured market (e.g., "USDT").
    /// </summary>
    public required string Market { get; init; }
}

/// <summary>
/// Information about a single position.
/// </summary>
public sealed record PositionInfo
{
    /// <summary>
    /// The trading pair.
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// Amount of the asset held.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Total cost of the position.
    /// </summary>
    public required decimal TotalCost { get; init; }

    /// <summary>
    /// Current market value of the position.
    /// </summary>
    public required decimal CurrentValue { get; init; }

    /// <summary>
    /// Average purchase price.
    /// </summary>
    public required decimal AveragePrice { get; init; }

    /// <summary>
    /// Current market price.
    /// </summary>
    public required decimal CurrentPrice { get; init; }

    /// <summary>
    /// Unrealized profit/loss in quote currency.
    /// </summary>
    public required decimal UnrealizedPnL { get; init; }

    /// <summary>
    /// Unrealized profit/loss as a percentage (margin).
    /// </summary>
    public required decimal Margin { get; init; }

    /// <summary>
    /// Dollar Cost Averaging level.
    /// </summary>
    public required int DCALevel { get; init; }

    /// <summary>
    /// Age of the position.
    /// </summary>
    public required TimeSpan Age { get; init; }

    /// <summary>
    /// When the position was first opened.
    /// </summary>
    public required DateTimeOffset OpenedAt { get; init; }

    /// <summary>
    /// Signal rule that triggered the position, if applicable.
    /// </summary>
    public string? SignalRule { get; init; }

    /// <summary>
    /// Whether there is a trailing sell order for this position.
    /// </summary>
    public bool HasTrailingSell { get; init; }
}

/// <summary>
/// Information about a historical order.
/// </summary>
public sealed record OrderHistoryInfo
{
    /// <summary>
    /// The order ID from the exchange.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// The trading pair.
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// Whether this was a buy or sell order.
    /// </summary>
    public required OrderSide Side { get; init; }

    /// <summary>
    /// The order result status.
    /// </summary>
    public required OrderStatus Status { get; init; }

    /// <summary>
    /// Amount ordered.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Amount actually filled.
    /// </summary>
    public required decimal AmountFilled { get; init; }

    /// <summary>
    /// Price per unit.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Average fill price.
    /// </summary>
    public required decimal AveragePrice { get; init; }

    /// <summary>
    /// Total cost/proceeds of the order.
    /// </summary>
    public required decimal TotalCost { get; init; }

    /// <summary>
    /// Fees paid for the order.
    /// </summary>
    public required decimal Fees { get; init; }

    /// <summary>
    /// When the order was placed.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Any message associated with the order.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Side of an order.
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// Status of an order.
/// </summary>
public enum OrderStatus
{
    Unknown,
    Filled,
    PartiallyFilled,
    Pending,
    Error,
    Canceled,
    Rejected,
    Expired,
    Open
}
