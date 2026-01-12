using System.Collections.Concurrent;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Trailing;

/// <summary>
/// Manages trailing stops for buy and sell orders.
/// Trailing stops follow the price in a favorable direction and trigger when price reverses.
/// </summary>
public sealed class TrailingManager
{
    private readonly IExchangePort _exchangePort;
    private readonly ConcurrentDictionary<TradingPair, BuyTrailingState> _buyTrailings = new();
    private readonly ConcurrentDictionary<TradingPair, SellTrailingState> _sellTrailings = new();

    public TrailingManager(IExchangePort exchangePort)
    {
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
    }

    /// <summary>
    /// Gets all active buy trailings.
    /// </summary>
    public IReadOnlyList<BuyTrailingState> ActiveBuyTrailings => _buyTrailings.Values.ToList();

    /// <summary>
    /// Gets all active sell trailings.
    /// </summary>
    public IReadOnlyList<SellTrailingState> ActiveSellTrailings => _sellTrailings.Values.ToList();

    /// <summary>
    /// Gets the count of active buy trailings.
    /// </summary>
    public int BuyTrailingCount => _buyTrailings.Count;

    /// <summary>
    /// Gets the count of active sell trailings.
    /// </summary>
    public int SellTrailingCount => _sellTrailings.Count;

    /// <summary>
    /// Checks if a pair has an active buy trailing.
    /// </summary>
    public bool HasBuyTrailing(TradingPair pair) => _buyTrailings.ContainsKey(pair);

    /// <summary>
    /// Checks if a pair has an active sell trailing.
    /// </summary>
    public bool HasSellTrailing(TradingPair pair) => _sellTrailings.ContainsKey(pair);

    /// <summary>
    /// Initiates a sell trailing for a position.
    /// Trailing follows price up and triggers sell when price drops by trailing percentage.
    /// </summary>
    public bool InitiateSellTrailing(
        PositionId positionId,
        TradingPair pair,
        Price currentPrice,
        decimal currentMargin,
        decimal targetMargin,
        TrailingConfig config)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(currentPrice);
        ArgumentNullException.ThrowIfNull(config);

        // Remove any existing buy trailing for this pair
        _buyTrailings.TryRemove(pair, out _);

        var state = new SellTrailingState
        {
            PositionId = positionId,
            Pair = pair,
            Config = config,
            TargetMargin = targetMargin,
            InitialPrice = currentPrice,
            InitialMargin = currentMargin,
            BestMargin = currentMargin,
            LastMargin = currentMargin
        };

        return _sellTrailings.TryAdd(pair, state);
    }

    /// <summary>
    /// Initiates a buy trailing for a new position or DCA.
    /// Trailing follows price down and triggers buy when price rises by trailing percentage.
    /// </summary>
    public bool InitiateBuyTrailing(
        TradingPair pair,
        Price currentPrice,
        Money cost,
        TrailingConfig config,
        PositionId? positionId = null,
        string? signalRule = null)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(currentPrice);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(config);

        // Remove any existing sell trailing for this pair
        _sellTrailings.TryRemove(pair, out _);

        var state = new BuyTrailingState
        {
            PositionId = positionId,
            Pair = pair,
            Config = config,
            Cost = cost,
            InitialPrice = currentPrice,
            InitialMargin = 0, // Buy trailing starts at 0 margin relative to initial price
            BestMargin = 0,
            LastMargin = 0,
            SignalRule = signalRule
        };

        return _buyTrailings.TryAdd(pair, state);
    }

    /// <summary>
    /// Cancels a sell trailing.
    /// </summary>
    public bool CancelSellTrailing(TradingPair pair)
    {
        return _sellTrailings.TryRemove(pair, out _);
    }

    /// <summary>
    /// Cancels a buy trailing.
    /// </summary>
    public bool CancelBuyTrailing(TradingPair pair)
    {
        return _buyTrailings.TryRemove(pair, out _);
    }

    /// <summary>
    /// Clears all trailing stops.
    /// </summary>
    public void ClearAll()
    {
        _buyTrailings.Clear();
        _sellTrailings.Clear();
    }

    /// <summary>
    /// Updates all sell trailings with current prices and returns triggered results.
    /// </summary>
    public IReadOnlyList<TrailingUpdateResult> UpdateSellTrailings(
        IReadOnlyDictionary<TradingPair, Price> currentPrices,
        Func<TradingPair, decimal, decimal> marginCalculator,
        Func<TradingPair, bool>? isTradingEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(currentPrices);
        ArgumentNullException.ThrowIfNull(marginCalculator);

        var results = new List<TrailingUpdateResult>();

        foreach (var kvp in _sellTrailings.ToArray())
        {
            var pair = kvp.Key;
            var state = kvp.Value;

            // Check if trading is still enabled
            if (isTradingEnabled != null && !isTradingEnabled(pair))
            {
                _sellTrailings.TryRemove(pair, out _);
                results.Add(new TrailingUpdateResult
                {
                    Type = TrailingType.Sell,
                    Pair = pair,
                    PositionId = state.PositionId,
                    Result = TrailingCheckResult.Disabled,
                    CurrentPrice = state.InitialPrice,
                    CurrentMargin = state.LastMargin,
                    BestMargin = state.BestMargin,
                    Config = state.Config,
                    Reason = "Trading disabled for pair"
                });
                continue;
            }

            // Get current price
            if (!currentPrices.TryGetValue(pair, out var currentPrice))
            {
                continue; // Skip if price not available
            }

            var currentMargin = marginCalculator(pair, currentPrice.Value);
            var result = ProcessSellTrailing(state, currentPrice, currentMargin);

            if (result.Result != TrailingCheckResult.Continue)
            {
                _sellTrailings.TryRemove(pair, out _);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Updates all buy trailings with current prices and returns triggered results.
    /// </summary>
    public IReadOnlyList<TrailingUpdateResult> UpdateBuyTrailings(
        IReadOnlyDictionary<TradingPair, Price> currentPrices,
        Func<TradingPair, bool>? isTradingEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(currentPrices);

        var results = new List<TrailingUpdateResult>();

        foreach (var kvp in _buyTrailings.ToArray())
        {
            var pair = kvp.Key;
            var state = kvp.Value;

            // Check if trading is still enabled
            if (isTradingEnabled != null && !isTradingEnabled(pair))
            {
                _buyTrailings.TryRemove(pair, out _);
                results.Add(new TrailingUpdateResult
                {
                    Type = TrailingType.Buy,
                    Pair = pair,
                    PositionId = state.PositionId,
                    Result = TrailingCheckResult.Disabled,
                    CurrentPrice = state.InitialPrice,
                    CurrentMargin = state.LastMargin,
                    BestMargin = state.BestMargin,
                    Config = state.Config,
                    Cost = state.Cost,
                    SignalRule = state.SignalRule,
                    Reason = "Trading disabled for pair"
                });
                continue;
            }

            // Get current price
            if (!currentPrices.TryGetValue(pair, out var currentPrice))
            {
                continue; // Skip if price not available
            }

            // Calculate margin relative to initial price
            var currentMargin = CalculateBuyMargin(state.InitialPrice.Value, currentPrice.Value);
            var result = ProcessBuyTrailing(state, currentPrice, currentMargin);

            if (result.Result != TrailingCheckResult.Continue)
            {
                _buyTrailings.TryRemove(pair, out _);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Updates a single sell trailing and returns the result.
    /// </summary>
    public TrailingUpdateResult UpdateSellTrailing(
        TradingPair pair,
        Price currentPrice,
        decimal currentMargin,
        bool isTradingEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(currentPrice);

        if (!_sellTrailings.TryGetValue(pair, out var state))
        {
            throw new InvalidOperationException($"No sell trailing exists for pair {pair}");
        }

        if (!isTradingEnabled)
        {
            _sellTrailings.TryRemove(pair, out _);
            return new TrailingUpdateResult
            {
                Type = TrailingType.Sell,
                Pair = pair,
                PositionId = state.PositionId,
                Result = TrailingCheckResult.Disabled,
                CurrentPrice = currentPrice,
                CurrentMargin = currentMargin,
                BestMargin = state.BestMargin,
                Config = state.Config,
                Reason = "Trading disabled for pair"
            };
        }

        var result = ProcessSellTrailing(state, currentPrice, currentMargin);

        if (result.Result != TrailingCheckResult.Continue)
        {
            _sellTrailings.TryRemove(pair, out _);
        }

        return result;
    }

    /// <summary>
    /// Updates a single buy trailing and returns the result.
    /// </summary>
    public TrailingUpdateResult UpdateBuyTrailing(
        TradingPair pair,
        Price currentPrice,
        bool isTradingEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(currentPrice);

        if (!_buyTrailings.TryGetValue(pair, out var state))
        {
            throw new InvalidOperationException($"No buy trailing exists for pair {pair}");
        }

        if (!isTradingEnabled)
        {
            _buyTrailings.TryRemove(pair, out _);
            return new TrailingUpdateResult
            {
                Type = TrailingType.Buy,
                Pair = pair,
                PositionId = state.PositionId,
                Result = TrailingCheckResult.Disabled,
                CurrentPrice = currentPrice,
                CurrentMargin = state.LastMargin,
                BestMargin = state.BestMargin,
                Config = state.Config,
                Cost = state.Cost,
                SignalRule = state.SignalRule,
                Reason = "Trading disabled for pair"
            };
        }

        var currentMargin = CalculateBuyMargin(state.InitialPrice.Value, currentPrice.Value);
        var result = ProcessBuyTrailing(state, currentPrice, currentMargin);

        if (result.Result != TrailingCheckResult.Continue)
        {
            _buyTrailings.TryRemove(pair, out _);
        }

        return result;
    }

    private TrailingUpdateResult ProcessSellTrailing(
        SellTrailingState state,
        Price currentPrice,
        decimal currentMargin)
    {
        // Check trigger conditions:
        // 1. Margin dropped to or below stop margin
        // 2. Margin dropped more than trailing percentage from best margin
        var shouldTrigger = currentMargin <= state.Config.StopMargin ||
                           currentMargin < (state.BestMargin - state.Config.TrailingPercentage);

        if (shouldTrigger)
        {
            // Determine action based on conditions
            if (currentMargin <= state.Config.StopMargin)
            {
                // Hit stop margin
                if (state.Config.StopAction == TrailingStopAction.Execute)
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Sell,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Triggered,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Reason = $"Stop margin {state.Config.StopMargin:F2}% reached at {currentMargin:F2}%"
                    };
                }
                else
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Sell,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Cancelled,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Reason = $"Stop margin {state.Config.StopMargin:F2}% reached - cancelled"
                    };
                }
            }
            else
            {
                // Trailing triggered - margin dropped from best by trailing percentage
                // Only sell if margin is positive or if target was negative
                if (currentMargin > 0 || state.TargetMargin < 0)
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Sell,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Triggered,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Reason = $"Trailing triggered: margin dropped from {state.BestMargin:F2}% to {currentMargin:F2}% (trailing: {state.Config.TrailingPercentage:F2}%)"
                    };
                }
                else
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Sell,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Cancelled,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Reason = "Trailing cancelled - margin went negative"
                    };
                }
            }
        }

        // Continue trailing - update state
        state.LastMargin = currentMargin;
        if (currentMargin > state.BestMargin)
        {
            state.BestMargin = currentMargin;
        }

        return new TrailingUpdateResult
        {
            Type = TrailingType.Sell,
            Pair = state.Pair,
            PositionId = state.PositionId,
            Result = TrailingCheckResult.Continue,
            CurrentPrice = currentPrice,
            CurrentMargin = currentMargin,
            BestMargin = state.BestMargin,
            Config = state.Config,
            Reason = $"Continuing - margin: {currentMargin:F2}%, best: {state.BestMargin:F2}%"
        };
    }

    private TrailingUpdateResult ProcessBuyTrailing(
        BuyTrailingState state,
        Price currentPrice,
        decimal currentMargin)
    {
        // Check trigger conditions (inverted from sell):
        // 1. Margin rose to or above stop margin (price went up too much)
        // 2. Margin rose more than trailing percentage from best margin (price bounced up)
        var shouldTrigger = currentMargin >= state.Config.StopMargin ||
                           currentMargin > (state.BestMargin + state.Config.TrailingPercentage);

        if (shouldTrigger)
        {
            // Determine action based on conditions
            if (currentMargin >= state.Config.StopMargin)
            {
                // Hit stop margin (price went up too much)
                if (state.Config.StopAction == TrailingStopAction.Execute)
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Buy,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Triggered,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Cost = state.Cost,
                        SignalRule = state.SignalRule,
                        Reason = $"Stop margin {state.Config.StopMargin:F2}% reached at {currentMargin:F2}%"
                    };
                }
                else
                {
                    return new TrailingUpdateResult
                    {
                        Type = TrailingType.Buy,
                        Pair = state.Pair,
                        PositionId = state.PositionId,
                        Result = TrailingCheckResult.Cancelled,
                        CurrentPrice = currentPrice,
                        CurrentMargin = currentMargin,
                        BestMargin = state.BestMargin,
                        Config = state.Config,
                        Cost = state.Cost,
                        SignalRule = state.SignalRule,
                        Reason = $"Stop margin {state.Config.StopMargin:F2}% reached - cancelled"
                    };
                }
            }
            else
            {
                // Trailing triggered - price bounced up from bottom
                return new TrailingUpdateResult
                {
                    Type = TrailingType.Buy,
                    Pair = state.Pair,
                    PositionId = state.PositionId,
                    Result = TrailingCheckResult.Triggered,
                    CurrentPrice = currentPrice,
                    CurrentMargin = currentMargin,
                    BestMargin = state.BestMargin,
                    Config = state.Config,
                    Cost = state.Cost,
                    SignalRule = state.SignalRule,
                    Reason = $"Trailing triggered: margin rose from {state.BestMargin:F2}% to {currentMargin:F2}% (trailing: {state.Config.TrailingPercentage:F2}%)"
                };
            }
        }

        // Continue trailing - update state
        state.LastMargin = currentMargin;
        if (currentMargin < state.BestMargin)
        {
            state.BestMargin = currentMargin;
        }

        return new TrailingUpdateResult
        {
            Type = TrailingType.Buy,
            Pair = state.Pair,
            PositionId = state.PositionId,
            Result = TrailingCheckResult.Continue,
            CurrentPrice = currentPrice,
            CurrentMargin = currentMargin,
            BestMargin = state.BestMargin,
            Config = state.Config,
            Cost = state.Cost,
            SignalRule = state.SignalRule,
            Reason = $"Continuing - margin: {currentMargin:F2}%, best: {state.BestMargin:F2}%"
        };
    }

    /// <summary>
    /// Calculates margin for buy trailing (percentage change from initial price).
    /// Negative margin means price has dropped (good for buying).
    /// </summary>
    private static decimal CalculateBuyMargin(decimal initialPrice, decimal currentPrice)
    {
        if (initialPrice == 0) return 0;
        return ((currentPrice - initialPrice) / initialPrice) * 100;
    }
}
