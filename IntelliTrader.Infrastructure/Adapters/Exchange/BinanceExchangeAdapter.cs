using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.ValueObjects;
using LegacyOrderSide = IntelliTrader.Core.OrderSide;
using LegacyOrderType = IntelliTrader.Core.OrderType;
using NewOrderSide = IntelliTrader.Application.Ports.Driven.OrderSide;
using NewOrderType = IntelliTrader.Application.Ports.Driven.OrderType;

namespace IntelliTrader.Infrastructure.Adapters.Exchange;

/// <summary>
/// Adapter that wraps the legacy IExchangeService to implement the new IExchangePort interface.
/// This allows the new hexagonal architecture to use the existing Binance integration.
/// </summary>
public sealed class BinanceExchangeAdapter : IExchangePort
{
    private readonly IExchangeService _legacyExchange;
    private readonly string _quoteCurrency;

    /// <summary>
    /// Creates a new BinanceExchangeAdapter.
    /// </summary>
    /// <param name="legacyExchange">The legacy exchange service to wrap.</param>
    /// <param name="quoteCurrency">The quote currency for the market (e.g., "USDT", "BTC").</param>
    public BinanceExchangeAdapter(IExchangeService legacyExchange, string quoteCurrency = "USDT")
    {
        _legacyExchange = legacyExchange ?? throw new ArgumentNullException(nameof(legacyExchange));
        _quoteCurrency = quoteCurrency ?? throw new ArgumentNullException(nameof(quoteCurrency));
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeOrderResult>> PlaceMarketBuyAsync(
        TradingPair pair,
        Money cost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(cost);

        try
        {
            // Get current price to calculate amount
            var currentPrice = await _legacyExchange.GetLastPrice(pair.Symbol);
            if (currentPrice <= 0)
            {
                return Result<ExchangeOrderResult>.Failure(
                    Error.ExchangeError($"Could not get current price for {pair.Symbol}"));
            }

            var amount = cost.Amount / currentPrice;

            var legacyOrder = new LegacyOrder
            {
                Side = LegacyOrderSide.Buy,
                Type = LegacyOrderType.Market,
                Date = DateTimeOffset.UtcNow,
                Pair = pair.Symbol,
                Amount = amount,
                Price = currentPrice
            };

            var result = await _legacyExchange.PlaceOrder(legacyOrder);
            return ConvertToExchangeOrderResult(result, pair);
        }
        catch (Exception ex)
        {
            return Result<ExchangeOrderResult>.Failure(
                Error.ExchangeError($"Failed to place market buy order: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeOrderResult>> PlaceMarketSellAsync(
        TradingPair pair,
        Quantity quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(quantity);

        try
        {
            var currentPrice = await _legacyExchange.GetLastPrice(pair.Symbol);
            if (currentPrice <= 0)
            {
                return Result<ExchangeOrderResult>.Failure(
                    Error.ExchangeError($"Could not get current price for {pair.Symbol}"));
            }

            var legacyOrder = new LegacyOrder
            {
                Side = LegacyOrderSide.Sell,
                Type = LegacyOrderType.Market,
                Date = DateTimeOffset.UtcNow,
                Pair = pair.Symbol,
                Amount = quantity.Value,
                Price = currentPrice
            };

            var result = await _legacyExchange.PlaceOrder(legacyOrder);
            return ConvertToExchangeOrderResult(result, pair);
        }
        catch (Exception ex)
        {
            return Result<ExchangeOrderResult>.Failure(
                Error.ExchangeError($"Failed to place market sell order: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeOrderResult>> PlaceLimitBuyAsync(
        TradingPair pair,
        Quantity quantity,
        Price price,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(price);

        try
        {
            var legacyOrder = new LegacyOrder
            {
                Side = LegacyOrderSide.Buy,
                Type = LegacyOrderType.Limit,
                Date = DateTimeOffset.UtcNow,
                Pair = pair.Symbol,
                Amount = quantity.Value,
                Price = price.Value
            };

            var result = await _legacyExchange.PlaceOrder(legacyOrder);
            return ConvertToExchangeOrderResult(result, pair);
        }
        catch (Exception ex)
        {
            return Result<ExchangeOrderResult>.Failure(
                Error.ExchangeError($"Failed to place limit buy order: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeOrderResult>> PlaceLimitSellAsync(
        TradingPair pair,
        Quantity quantity,
        Price price,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(price);

        try
        {
            var legacyOrder = new LegacyOrder
            {
                Side = LegacyOrderSide.Sell,
                Type = LegacyOrderType.Limit,
                Date = DateTimeOffset.UtcNow,
                Pair = pair.Symbol,
                Amount = quantity.Value,
                Price = price.Value
            };

            var result = await _legacyExchange.PlaceOrder(legacyOrder);
            return ConvertToExchangeOrderResult(result, pair);
        }
        catch (Exception ex)
        {
            return Result<ExchangeOrderResult>.Failure(
                Error.ExchangeError($"Failed to place limit sell order: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<Price>> GetCurrentPriceAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);

        try
        {
            var price = await _legacyExchange.GetLastPrice(pair.Symbol);
            if (price <= 0)
            {
                return Result<Price>.Failure(
                    Error.NotFound("Price", pair.Symbol));
            }

            return Result<Price>.Success(Price.Create(price));
        }
        catch (Exception ex)
        {
            return Result<Price>.Failure(
                Error.ExchangeError($"Failed to get price for {pair.Symbol}: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<TradingPair, Price>>> GetCurrentPricesAsync(
        IEnumerable<TradingPair> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        try
        {
            var tickers = await _legacyExchange.GetTickers(_quoteCurrency);
            var tickerDict = tickers.ToDictionary(t => t.Pair, t => t.LastPrice);

            var result = new Dictionary<TradingPair, Price>();
            foreach (var pair in pairs)
            {
                if (tickerDict.TryGetValue(pair.Symbol, out var price) && price > 0)
                {
                    result[pair] = Price.Create(price);
                }
            }

            return Result<IReadOnlyDictionary<TradingPair, Price>>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<TradingPair, Price>>.Failure(
                Error.ExchangeError($"Failed to get prices: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeBalance>> GetBalanceAsync(
        string currency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);

        try
        {
            var amounts = await _legacyExchange.GetAvailableAmounts();

            if (!amounts.TryGetValue(currency, out var available))
            {
                // Return zero balance if currency not found
                return Result<ExchangeBalance>.Success(new ExchangeBalance
                {
                    Currency = currency,
                    Total = 0,
                    Available = 0,
                    Locked = 0
                });
            }

            // Legacy API only provides available amounts, not total/locked
            return Result<ExchangeBalance>.Success(new ExchangeBalance
            {
                Currency = currency,
                Total = available,
                Available = available,
                Locked = 0
            });
        }
        catch (Exception ex)
        {
            return Result<ExchangeBalance>.Failure(
                Error.ExchangeError($"Failed to get balance for {currency}: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ExchangeBalance>>> GetAllBalancesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var amounts = await _legacyExchange.GetAvailableAmounts();

            var balances = amounts
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => new ExchangeBalance
                {
                    Currency = kvp.Key,
                    Total = kvp.Value,
                    Available = kvp.Value,
                    Locked = 0
                })
                .ToList();

            return Result<IReadOnlyList<ExchangeBalance>>.Success(balances);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ExchangeBalance>>.Failure(
                Error.ExchangeError($"Failed to get balances: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeOrderInfo>> GetOrderAsync(
        TradingPair pair,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(orderId);

        try
        {
            var trades = await _legacyExchange.GetMyTrades(pair.Symbol);
            var trade = trades.FirstOrDefault(t => t.OrderId == orderId);

            if (trade == null)
            {
                return Result<ExchangeOrderInfo>.Failure(
                    Error.NotFound("Order", orderId));
            }

            return Result<ExchangeOrderInfo>.Success(new ExchangeOrderInfo
            {
                OrderId = trade.OrderId,
                Pair = pair,
                Side = ConvertOrderSide(trade.Side),
                Type = NewOrderType.Market, // Legacy doesn't expose order type
                Status = ConvertOrderResult(trade.Result),
                OriginalQuantity = Quantity.Create(trade.Amount),
                FilledQuantity = Quantity.Create(trade.AmountFilled),
                Price = Price.Create(trade.Price),
                AveragePrice = Price.Create(trade.AveragePrice),
                Fees = Money.Create(trade.Fees, trade.FeesCurrency ?? _quoteCurrency),
                CreatedAt = trade.Date
            });
        }
        catch (Exception ex)
        {
            return Result<ExchangeOrderInfo>.Failure(
                Error.ExchangeError($"Failed to get order {orderId}: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<Result> CancelOrderAsync(
        TradingPair pair,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        // Legacy exchange service doesn't expose cancel order functionality
        // This would need to be added to the legacy service or implemented directly with the Binance API
        return Task.FromResult(Result.Failure(
            new Error("NotSupported", "Cancel order is not supported by the legacy exchange service")));
    }

    /// <inheritdoc />
    public Task<Result<TradingPairRules>> GetTradingRulesAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);

        // The legacy exchange service doesn't expose trading rules directly
        // Return sensible defaults for Binance
        // In a full implementation, these would be fetched from the exchange
        var rules = new TradingPairRules
        {
            Pair = pair,
            MinOrderValue = 10m, // Binance minimum order value in USDT
            MinQuantity = 0.00001m,
            MaxQuantity = 9999999m,
            QuantityStepSize = 0.00001m,
            PricePrecision = 8,
            QuantityPrecision = 8,
            MinPrice = 0.00000001m,
            MaxPrice = 999999m,
            IsTradingEnabled = true
        };

        return Task.FromResult(Result<TradingPairRules>.Success(rules));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> TestConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get tickers as a connectivity test
            var tickers = await _legacyExchange.GetTickers(_quoteCurrency);
            return Result<bool>.Success(tickers.Any());
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(
                Error.ExchangeError($"Failed to connect to exchange: {ex.Message}"));
        }
    }

    private Result<ExchangeOrderResult> ConvertToExchangeOrderResult(IOrderDetails legacyResult, TradingPair pair)
    {
        if (legacyResult == null)
        {
            return Result<ExchangeOrderResult>.Failure(
                Error.ExchangeError("Order result was null"));
        }

        var status = ConvertOrderResult(legacyResult.Result);
        var feesCurrency = legacyResult.FeesCurrency ?? _quoteCurrency;

        return Result<ExchangeOrderResult>.Success(new ExchangeOrderResult
        {
            OrderId = legacyResult.OrderId,
            Pair = pair,
            Side = ConvertOrderSide(legacyResult.Side),
            Type = NewOrderType.Market, // Legacy doesn't expose this, default to Market
            Status = status,
            RequestedQuantity = Quantity.Create(legacyResult.Amount),
            FilledQuantity = Quantity.Create(legacyResult.AmountFilled),
            Price = Price.Create(legacyResult.Price),
            AveragePrice = Price.Create(legacyResult.AveragePrice),
            Cost = Money.Create(legacyResult.AverageCost, _quoteCurrency),
            Fees = Money.Create(legacyResult.Fees, feesCurrency),
            Timestamp = legacyResult.Date
        });
    }

    private static NewOrderSide ConvertOrderSide(LegacyOrderSide side)
    {
        return side switch
        {
            LegacyOrderSide.Buy => NewOrderSide.Buy,
            LegacyOrderSide.Sell => NewOrderSide.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown order side")
        };
    }

    private static OrderStatus ConvertOrderResult(OrderResult result)
    {
        return result switch
        {
            OrderResult.Pending => OrderStatus.New,
            OrderResult.Filled => OrderStatus.Filled,
            OrderResult.FilledPartially => OrderStatus.PartiallyFilled,
            OrderResult.Canceled => OrderStatus.Canceled,
            OrderResult.Error => OrderStatus.Rejected,
            OrderResult.Unknown => OrderStatus.Rejected,
            _ => OrderStatus.Rejected
        };
    }
}

/// <summary>
/// Internal order implementation for the legacy exchange service.
/// </summary>
internal sealed class LegacyOrder : IOrder
{
    public LegacyOrderSide Side { get; init; }
    public LegacyOrderType Type { get; init; }
    public DateTimeOffset Date { get; init; }
    public required string Pair { get; init; }
    public decimal Amount { get; init; }
    public decimal Price { get; init; }
}
