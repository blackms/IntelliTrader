namespace IntelliTrader.Application.Trading.Queries;

/// <summary>
/// Query to get available trading pairs from the legacy trading service.
/// This bridges the Application layer to the legacy service for read operations.
/// </summary>
public sealed record GetTradingPairsQuery
{
    /// <summary>
    /// Optional market filter (e.g., "USDT", "BTC").
    /// If specified, only pairs in this market are returned.
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Optional search term to filter pairs.
    /// </summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Whether to include only pairs with active positions.
    /// </summary>
    public bool OnlyWithPositions { get; init; } = false;

    /// <summary>
    /// Whether to include current price information.
    /// </summary>
    public bool IncludePrices { get; init; } = false;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int? Limit { get; init; }
}

/// <summary>
/// Result of the trading pairs query.
/// </summary>
public sealed record GetTradingPairsResult
{
    /// <summary>
    /// The list of trading pairs.
    /// </summary>
    public required IReadOnlyList<TradingPairInfo> Pairs { get; init; }

    /// <summary>
    /// Total count of available pairs (before limit).
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// The market that was queried, if a filter was applied.
    /// </summary>
    public string? Market { get; init; }
}

/// <summary>
/// Information about a single trading pair.
/// </summary>
public sealed record TradingPairInfo
{
    /// <summary>
    /// The trading pair symbol (e.g., "BTCUSDT").
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// The base asset (e.g., "BTC").
    /// </summary>
    public required string BaseAsset { get; init; }

    /// <summary>
    /// The quote asset/market (e.g., "USDT").
    /// </summary>
    public required string QuoteAsset { get; init; }

    /// <summary>
    /// Current price, if requested.
    /// </summary>
    public decimal? CurrentPrice { get; init; }

    /// <summary>
    /// 24-hour price change percentage, if available.
    /// </summary>
    public decimal? PriceChangePercent24h { get; init; }

    /// <summary>
    /// 24-hour trading volume, if available.
    /// </summary>
    public decimal? Volume24h { get; init; }

    /// <summary>
    /// Whether there is an active position for this pair.
    /// </summary>
    public bool HasPosition { get; init; }

    /// <summary>
    /// Whether there is a trailing buy order for this pair.
    /// </summary>
    public bool HasTrailingBuy { get; init; }

    /// <summary>
    /// Whether there is a trailing sell order for this pair.
    /// </summary>
    public bool HasTrailingSell { get; init; }
}
