using IntelliTrader.Application.Common;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Port for interacting with cryptocurrency exchanges.
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface IExchangePort
{
    /// <summary>
    /// Places a market buy order.
    /// </summary>
    Task<Result<ExchangeOrderResult>> PlaceMarketBuyAsync(
        TradingPair pair,
        Money cost,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a market sell order.
    /// </summary>
    Task<Result<ExchangeOrderResult>> PlaceMarketSellAsync(
        TradingPair pair,
        Quantity quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a limit buy order.
    /// </summary>
    Task<Result<ExchangeOrderResult>> PlaceLimitBuyAsync(
        TradingPair pair,
        Quantity quantity,
        Price price,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a limit sell order.
    /// </summary>
    Task<Result<ExchangeOrderResult>> PlaceLimitSellAsync(
        TradingPair pair,
        Quantity quantity,
        Price price,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current price for a trading pair.
    /// </summary>
    Task<Result<Price>> GetCurrentPriceAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current prices for multiple trading pairs.
    /// </summary>
    Task<Result<IReadOnlyDictionary<TradingPair, Price>>> GetCurrentPricesAsync(
        IEnumerable<TradingPair> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the account balance for a specific currency.
    /// </summary>
    Task<Result<ExchangeBalance>> GetBalanceAsync(
        string currency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all account balances.
    /// </summary>
    Task<Result<IReadOnlyList<ExchangeBalance>>> GetAllBalancesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets order details by ID.
    /// </summary>
    Task<Result<ExchangeOrderInfo>> GetOrderAsync(
        TradingPair pair,
        string orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an open order.
    /// </summary>
    Task<Result> CancelOrderAsync(
        TradingPair pair,
        string orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets trading rules for a pair (min order size, price precision, etc.).
    /// </summary>
    Task<Result<TradingPairRules>> GetTradingRulesAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to the exchange.
    /// </summary>
    Task<Result<bool>> TestConnectivityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of placing an order on the exchange.
/// </summary>
public sealed record ExchangeOrderResult
{
    public required string OrderId { get; init; }
    public required TradingPair Pair { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required OrderStatus Status { get; init; }
    public required Quantity RequestedQuantity { get; init; }
    public required Quantity FilledQuantity { get; init; }
    public required Price Price { get; init; }
    public required Price AveragePrice { get; init; }
    public required Money Cost { get; init; }
    public required Money Fees { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    public bool IsFullyFilled => Status == OrderStatus.Filled;
    public bool IsPartiallyFilled => Status == OrderStatus.PartiallyFilled;
}

/// <summary>
/// Information about an existing order.
/// </summary>
public sealed record ExchangeOrderInfo
{
    public required string OrderId { get; init; }
    public required TradingPair Pair { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required OrderStatus Status { get; init; }
    public required Quantity OriginalQuantity { get; init; }
    public required Quantity FilledQuantity { get; init; }
    public required Price Price { get; init; }
    public required Price AveragePrice { get; init; }
    public required Money Fees { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Account balance for a currency.
/// </summary>
public sealed record ExchangeBalance
{
    public required string Currency { get; init; }
    public required decimal Total { get; init; }
    public required decimal Available { get; init; }
    public required decimal Locked { get; init; }
}

/// <summary>
/// Trading rules for a pair.
/// </summary>
public sealed record TradingPairRules
{
    public required TradingPair Pair { get; init; }
    public required decimal MinOrderValue { get; init; }
    public required decimal MinQuantity { get; init; }
    public required decimal MaxQuantity { get; init; }
    public required decimal QuantityStepSize { get; init; }
    public required int PricePrecision { get; init; }
    public required int QuantityPrecision { get; init; }
    public required decimal MinPrice { get; init; }
    public required decimal MaxPrice { get; init; }
    public bool IsTradingEnabled { get; init; } = true;
}

/// <summary>
/// Order side (buy/sell).
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>
/// Order type.
/// </summary>
public enum OrderType
{
    Market,
    Limit,
    StopLoss,
    StopLossLimit,
    TakeProfit,
    TakeProfitLimit
}

/// <summary>
/// Order status.
/// </summary>
public enum OrderStatus
{
    New,
    PartiallyFilled,
    Filled,
    Canceled,
    PendingCancel,
    Rejected,
    Expired
}
