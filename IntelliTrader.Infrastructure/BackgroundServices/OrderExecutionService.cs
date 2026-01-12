using Microsoft.Extensions.Logging;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Configuration for the OrderExecutionService.
/// </summary>
public sealed class OrderExecutionOptions
{
    /// <summary>
    /// Interval between order processing cycles. Default: 1 second.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Delay before starting order processing. Default: 0 seconds.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether virtual trading is enabled.
    /// </summary>
    public bool VirtualTrading { get; set; } = true;

    /// <summary>
    /// The quote currency for the market.
    /// </summary>
    public string QuoteCurrency { get; set; } = "USDT";
}

/// <summary>
/// Event args for when an order is executed.
/// </summary>
public sealed class OrderExecutedEventArgs : EventArgs
{
    public TradingPair Pair { get; init; } = null!;
    public OrderSide Side { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal Cost { get; init; }
    public bool IsVirtual { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsSuccess => ErrorMessage == null;
}

/// <summary>
/// Background service that processes trading pairs and executes orders.
/// Handles trailing stops for both buys and sells.
/// Replaces the legacy TradingTimedTask.
/// </summary>
public class OrderExecutionService : TimedBackgroundService
{
    private readonly ILogger<OrderExecutionService> _logger;
    private readonly IExchangePort _exchangePort;
    private readonly IPositionRepository _positionRepository;
    private readonly TrailingManager _trailingManager;
    private readonly TradingRuleProcessorService? _ruleProcessor;
    private readonly OrderExecutionOptions _options;

    /// <summary>
    /// Event raised when an order is executed.
    /// </summary>
    public event EventHandler<OrderExecutedEventArgs>? OrderExecuted;

    /// <summary>
    /// Gets or sets whether logging is enabled.
    /// </summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// Creates a new OrderExecutionService.
    /// </summary>
    public OrderExecutionService(
        ILogger<OrderExecutionService> logger,
        IExchangePort exchangePort,
        IPositionRepository positionRepository,
        TrailingManager trailingManager,
        TradingRuleProcessorService? ruleProcessor = null,
        OrderExecutionOptions? options = null)
        : base(logger, options?.Interval ?? TimeSpan.FromSeconds(1), options?.StartDelay)
    {
        _logger = logger;
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _trailingManager = trailingManager ?? throw new ArgumentNullException(nameof(trailingManager));
        _ruleProcessor = ruleProcessor;
        _options = options ?? new OrderExecutionOptions();
    }

    /// <inheritdoc />
    protected override async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessTradingPairsAsync(stoppingToken);
            await ProcessTrailingBuysAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in order execution service");
        }
    }

    private async Task ProcessTradingPairsAsync(CancellationToken cancellationToken)
    {
        var positions = await _positionRepository.GetAllActiveAsync(cancellationToken);

        foreach (var position in positions)
        {
            await ProcessPositionAsync(position, cancellationToken);
        }
    }

    private async Task ProcessPositionAsync(Position position, CancellationToken cancellationToken)
    {
        // Get current price
        var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
        if (priceResult.IsFailure)
        {
            _logger.LogWarning("Failed to get price for {Pair}", position.Pair.Symbol);
            return;
        }

        var currentPrice = priceResult.Value;
        var currentMargin = position.CalculateMargin(currentPrice);

        // Get pair configuration
        var pairConfig = _ruleProcessor?.GetPairConfig(position.Pair);

        // Check if we're trailing this position
        if (_trailingManager.HasSellTrailing(position.Pair))
        {
            await ProcessSellTrailingAsync(position, currentPrice, currentMargin.Percentage, cancellationToken);
        }
        else if (pairConfig?.SellEnabled == true)
        {
            // Check sell conditions
            if (currentMargin.Percentage >= (pairConfig.SellMargin))
            {
                await InitiateSellAsync(position, currentPrice, pairConfig, cancellationToken);
            }
            else if (pairConfig.SellStopLossEnabled &&
                     currentMargin.Percentage <= pairConfig.SellStopLossMargin &&
                     position.Age >= pairConfig.SellStopLossMinAge)
            {
                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Stop loss triggered for {Pair}. Margin: {Margin:F2}%",
                        position.Pair.Symbol, currentMargin.Percentage);
                }
                await PlaceSellOrderAsync(position, currentPrice, cancellationToken);
            }
            else if (pairConfig.DCAEnabled && pairConfig.NextDCAMargin.HasValue &&
                     currentMargin.Percentage <= pairConfig.NextDCAMargin.Value &&
                     !_trailingManager.HasBuyTrailing(position.Pair))
            {
                // DCA trigger
                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "DCA triggered for {Pair}. Margin: {Margin:F2}%, Level: {Level}",
                        position.Pair.Symbol, currentMargin.Percentage, pairConfig.CurrentDCALevel + 1);
                }
                await InitiateDCABuyAsync(position, currentPrice, pairConfig, cancellationToken);
            }
        }
    }

    private async Task ProcessSellTrailingAsync(
        Position position,
        Price currentPrice,
        decimal currentMargin,
        CancellationToken cancellationToken)
    {
        var result = _trailingManager.UpdateSellTrailing(position.Pair, currentPrice, currentMargin);

        switch (result.Result)
        {
            case TrailingCheckResult.Continue:
                if (LoggingEnabled)
                {
                    _logger.LogDebug(
                        "Continue trailing sell {Pair}. Price: {Price:F8}, Margin: {Margin:F2}%",
                        position.Pair.Symbol, currentPrice.Value, currentMargin);
                }
                break;

            case TrailingCheckResult.Triggered:
                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Trailing sell triggered for {Pair}. Margin: {Margin:F2}%",
                        position.Pair.Symbol, currentMargin);
                }
                await PlaceSellOrderAsync(position, currentPrice, cancellationToken);
                break;

            case TrailingCheckResult.Cancelled:
                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Cancel trailing sell {Pair}. Reason: {Reason}",
                        position.Pair.Symbol, result.Reason);
                }
                break;

            case TrailingCheckResult.Disabled:
                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Stop trailing sell {Pair}. Reason: trading disabled",
                        position.Pair.Symbol);
                }
                break;
        }
    }

    private async Task ProcessTrailingBuysAsync(CancellationToken cancellationToken)
    {
        foreach (var state in _trailingManager.ActiveBuyTrailings.ToList())
        {
            var priceResult = await _exchangePort.GetCurrentPriceAsync(state.Pair, cancellationToken);
            if (priceResult.IsFailure)
            {
                continue;
            }

            var currentPrice = priceResult.Value;
            var result = _trailingManager.UpdateBuyTrailing(state.Pair, currentPrice);

            switch (result.Result)
            {
                case TrailingCheckResult.Continue:
                    if (LoggingEnabled)
                    {
                        _logger.LogDebug(
                            "Continue trailing buy {Pair}. Price: {Price:F8}, Margin: {Margin:F2}%",
                            state.Pair.Symbol, currentPrice.Value, result.CurrentMargin);
                    }
                    break;

                case TrailingCheckResult.Triggered:
                    if (LoggingEnabled)
                    {
                        _logger.LogInformation(
                            "Trailing buy triggered for {Pair}. Margin: {Margin:F2}%",
                            state.Pair.Symbol, result.CurrentMargin);
                    }
                    await PlaceBuyOrderAsync(state.Pair, currentPrice, state.Cost.Amount, cancellationToken);
                    break;

                case TrailingCheckResult.Cancelled:
                case TrailingCheckResult.Disabled:
                    if (LoggingEnabled)
                    {
                        _logger.LogInformation(
                            "Stop trailing buy {Pair}. Reason: {Reason}",
                            state.Pair.Symbol, result.Reason);
                    }
                    break;
            }
        }
    }

    private async Task InitiateSellAsync(
        Position position,
        Price currentPrice,
        PairTradingConfig config,
        CancellationToken cancellationToken)
    {
        if (config.SellTrailing > 0)
        {
            var currentMargin = position.CalculateMargin(currentPrice);

            var trailingConfig = new TrailingConfig
            {
                TrailingPercentage = config.SellTrailing,
                StopMargin = config.SellTrailingStopMargin,
                StopAction = TrailingStopAction.Execute
            };

            _trailingManager.InitiateSellTrailing(
                position.Id,
                position.Pair,
                currentPrice,
                currentMargin.Percentage,
                config.SellMargin,
                trailingConfig);

            if (LoggingEnabled)
            {
                _logger.LogInformation(
                    "Start trailing sell {Pair}. Price: {Price:F8}, Margin: {Margin:F2}%",
                    position.Pair.Symbol, currentPrice.Value, currentMargin.Percentage);
            }
        }
        else
        {
            await PlaceSellOrderAsync(position, currentPrice, cancellationToken);
        }
    }

    private async Task InitiateDCABuyAsync(
        Position position,
        Price currentPrice,
        PairTradingConfig config,
        CancellationToken cancellationToken)
    {
        var dcaCost = position.TotalCost.Amount * config.BuyMultiplier;

        if (config.BuyTrailing > 0)
        {
            var trailingConfig = new TrailingConfig
            {
                TrailingPercentage = config.BuyTrailing,
                StopMargin = config.BuyTrailingStopMargin,
                StopAction = TrailingStopAction.Execute
            };

            _trailingManager.InitiateBuyTrailing(
                position.Pair,
                currentPrice,
                Money.Create(dcaCost, _options.QuoteCurrency),
                trailingConfig,
                position.Id);

            if (LoggingEnabled)
            {
                _logger.LogInformation(
                    "Start trailing DCA buy {Pair}. Price: {Price:F8}",
                    position.Pair.Symbol, currentPrice.Value);
            }
        }
        else
        {
            await PlaceBuyOrderAsync(position.Pair, currentPrice, dcaCost, cancellationToken);
        }
    }

    private async Task PlaceSellOrderAsync(
        Position position,
        Price currentPrice,
        CancellationToken cancellationToken)
    {
        _trailingManager.CancelSellTrailing(position.Pair);
        _trailingManager.CancelBuyTrailing(position.Pair);

        if (_options.VirtualTrading)
        {
            // Virtual sell
            var sellFees = Money.Create(position.TotalQuantity.Value * currentPrice.Value * 0.001m, _options.QuoteCurrency);
            position.Close(OrderId.From(DateTime.UtcNow.Ticks.ToString()), currentPrice, sellFees);
            await _positionRepository.SaveAsync(position, cancellationToken);

            var eventArgs = new OrderExecutedEventArgs
            {
                Pair = position.Pair,
                Side = OrderSide.Sell,
                Price = currentPrice.Value,
                Quantity = position.TotalQuantity.Value,
                Cost = position.TotalQuantity.Value * currentPrice.Value,
                IsVirtual = true,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (LoggingEnabled)
            {
                _logger.LogInformation(
                    "Virtual sell order for {Pair}. Price: {Price:F8}, Amount: {Amount:F8}",
                    position.Pair.Symbol, currentPrice.Value, position.TotalQuantity.Value);
            }

            OrderExecuted?.Invoke(this, eventArgs);
        }
        else
        {
            // Real sell order
            var result = await _exchangePort.PlaceMarketSellAsync(
                position.Pair, position.TotalQuantity, cancellationToken);

            if (result.IsSuccess)
            {
                var order = result.Value;
                position.Close(OrderId.From(order.OrderId), order.AveragePrice, order.Fees);
                await _positionRepository.SaveAsync(position, cancellationToken);

                var eventArgs = new OrderExecutedEventArgs
                {
                    Pair = position.Pair,
                    Side = OrderSide.Sell,
                    Price = order.AveragePrice.Value,
                    Quantity = order.FilledQuantity.Value,
                    Cost = order.AveragePrice.Value * order.FilledQuantity.Value,
                    IsVirtual = false,
                    Timestamp = DateTimeOffset.UtcNow
                };

                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Sell order executed for {Pair}. Price: {Price:F8}, Amount: {Amount:F8}",
                        position.Pair.Symbol, order.AveragePrice.Value, order.FilledQuantity.Value);
                }

                OrderExecuted?.Invoke(this, eventArgs);
            }
            else
            {
                _logger.LogError("Failed to place sell order for {Pair}: {Error}",
                    position.Pair.Symbol, result.Error.Message);

                OrderExecuted?.Invoke(this, new OrderExecutedEventArgs
                {
                    Pair = position.Pair,
                    Side = OrderSide.Sell,
                    Price = currentPrice.Value,
                    Quantity = position.TotalQuantity.Value,
                    Cost = 0,
                    IsVirtual = false,
                    Timestamp = DateTimeOffset.UtcNow,
                    ErrorMessage = result.Error.Message
                });
            }
        }
    }

    private async Task PlaceBuyOrderAsync(
        TradingPair pair,
        Price currentPrice,
        decimal maxCost,
        CancellationToken cancellationToken)
    {
        _trailingManager.CancelBuyTrailing(pair);

        var quantity = Quantity.Create(maxCost / currentPrice.Value);

        if (_options.VirtualTrading)
        {
            // Virtual buy - check if this is a DCA or new position
            var existingPosition = await _positionRepository.GetByPairAsync(pair, cancellationToken);
            var buyFees = Money.Create(quantity.Value * currentPrice.Value * 0.001m, _options.QuoteCurrency);

            if (existingPosition != null)
            {
                // DCA
                existingPosition.AddDCAEntry(
                    OrderId.From(DateTime.UtcNow.Ticks.ToString()),
                    currentPrice,
                    quantity,
                    buyFees);
                await _positionRepository.SaveAsync(existingPosition, cancellationToken);
            }
            else
            {
                // New position
                var newPosition = Position.Open(
                    pair,
                    OrderId.From(DateTime.UtcNow.Ticks.ToString()),
                    currentPrice,
                    quantity,
                    buyFees);
                await _positionRepository.SaveAsync(newPosition, cancellationToken);
            }

            var eventArgs = new OrderExecutedEventArgs
            {
                Pair = pair,
                Side = OrderSide.Buy,
                Price = currentPrice.Value,
                Quantity = quantity.Value,
                Cost = quantity.Value * currentPrice.Value,
                IsVirtual = true,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (LoggingEnabled)
            {
                _logger.LogInformation(
                    "Virtual buy order for {Pair}. Price: {Price:F8}, Amount: {Amount:F8}, Cost: {Cost:F8}",
                    pair.Symbol, currentPrice.Value, quantity.Value, maxCost);
            }

            OrderExecuted?.Invoke(this, eventArgs);
        }
        else
        {
            // Real buy order - use Money for cost
            var cost = Money.Create(maxCost, _options.QuoteCurrency);
            var result = await _exchangePort.PlaceMarketBuyAsync(pair, cost, cancellationToken);

            if (result.IsSuccess)
            {
                var order = result.Value;

                var existingPosition = await _positionRepository.GetByPairAsync(pair, cancellationToken);
                if (existingPosition != null)
                {
                    existingPosition.AddDCAEntry(
                        OrderId.From(order.OrderId),
                        order.AveragePrice,
                        order.FilledQuantity,
                        order.Fees);
                    await _positionRepository.SaveAsync(existingPosition, cancellationToken);
                }
                else
                {
                    var newPosition = Position.Open(
                        pair,
                        OrderId.From(order.OrderId),
                        order.AveragePrice,
                        order.FilledQuantity,
                        order.Fees);
                    await _positionRepository.SaveAsync(newPosition, cancellationToken);
                }

                var eventArgs = new OrderExecutedEventArgs
                {
                    Pair = pair,
                    Side = OrderSide.Buy,
                    Price = order.AveragePrice.Value,
                    Quantity = order.FilledQuantity.Value,
                    Cost = order.AveragePrice.Value * order.FilledQuantity.Value,
                    IsVirtual = false,
                    Timestamp = DateTimeOffset.UtcNow
                };

                if (LoggingEnabled)
                {
                    _logger.LogInformation(
                        "Buy order executed for {Pair}. Price: {Price:F8}, Amount: {Amount:F8}",
                        pair.Symbol, order.AveragePrice.Value, order.FilledQuantity.Value);
                }

                OrderExecuted?.Invoke(this, eventArgs);
            }
            else
            {
                _logger.LogError("Failed to place buy order for {Pair}: {Error}",
                    pair.Symbol, result.Error.Message);

                OrderExecuted?.Invoke(this, new OrderExecutedEventArgs
                {
                    Pair = pair,
                    Side = OrderSide.Buy,
                    Price = currentPrice.Value,
                    Quantity = quantity.Value,
                    Cost = 0,
                    IsVirtual = false,
                    Timestamp = DateTimeOffset.UtcNow,
                    ErrorMessage = result.Error.Message
                });
            }
        }
    }

    private static decimal CalculateBuyMargin(decimal initialPrice, decimal currentPrice)
    {
        if (initialPrice == 0) return 0;
        return ((currentPrice - initialPrice) / initialPrice) * 100;
    }

    /// <summary>
    /// Clears all trailing states.
    /// </summary>
    public void ClearTrailing()
    {
        _trailingManager.ClearAll();
    }

    /// <summary>
    /// Gets the list of pairs with active trailing buys.
    /// </summary>
    public IReadOnlyList<string> GetTrailingBuys()
    {
        return _trailingManager.ActiveBuyTrailings
            .Select(x => x.Pair.Symbol)
            .ToList();
    }

    /// <summary>
    /// Gets the list of pairs with active trailing sells.
    /// </summary>
    public IReadOnlyList<string> GetTrailingSells()
    {
        return _trailingManager.ActiveSellTrailings
            .Select(x => x.Pair.Symbol)
            .ToList();
    }
}
