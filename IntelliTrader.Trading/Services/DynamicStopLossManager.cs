using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Manages dynamic ATR-based trailing stop-loss calculations for trading pairs.
    /// Tracks highest/lowest prices for trailing functionality and maintains state per pair.
    /// </summary>
    internal class DynamicStopLossManager : IDynamicStopLossManager
    {
        private readonly IATRCalculator _atrCalculator;
        private readonly ILoggingService _loggingService;
        private readonly ConcurrentDictionary<string, StopLossResult> _pairStates;

        public DynamicStopLossManager(IATRCalculator atrCalculator, ILoggingService loggingService)
        {
            _atrCalculator = atrCalculator ?? throw new ArgumentNullException(nameof(atrCalculator));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _pairStates = new ConcurrentDictionary<string, StopLossResult>();
        }

        /// <inheritdoc />
        public StopLossResult CalculateStopLoss(
            string pair,
            decimal entryPrice,
            decimal currentPrice,
            IEnumerable<Candle> candles,
            IStopLossConfig config,
            bool isLong = true)
        {
            if (string.IsNullOrEmpty(pair))
            {
                throw new ArgumentNullException(nameof(pair));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var result = new StopLossResult
            {
                Pair = pair,
                Type = config.Type,
                HighestPrice = currentPrice,
                LowestPrice = currentPrice
            };

            // For Fixed type, use the legacy percentage-based stop-loss
            if (config.Type == StopLossType.Fixed)
            {
                result.StopLossPercent = config.MinimumPercent;
                result.StopLossPrice = CalculateFixedStopLoss(entryPrice, config.MinimumPercent, isLong);
                result.IsTriggered = IsStopLossTriggered(currentPrice, result, isLong);
                result.IsMinimumApplied = true;

                _pairStates[pair] = result;
                return result;
            }

            // ATR-based stop-loss calculation
            var candleList = candles?.ToList() ?? new List<Candle>();

            // Calculate ATR
            result.ATRValue = _atrCalculator.CalculateATR(candleList, config.ATRPeriod);

            // Determine if market is trending for multiplier selection
            result.IsTrending = _atrCalculator.IsTrending(candleList);

            // Get the appropriate multiplier
            result.ATRMultiplier = GetRecommendedMultiplier(candleList, config);

            // Calculate ATR-based stop-loss
            decimal atrStopLossPercent = 0m;
            decimal atrStopLossPrice = 0m;

            if (result.ATRValue > 0)
            {
                atrStopLossPercent = _atrCalculator.CalculateStopLossPercent(currentPrice, result.ATRValue, result.ATRMultiplier);
                atrStopLossPrice = _atrCalculator.CalculateStopLoss(currentPrice, result.ATRValue, result.ATRMultiplier, isLong);
            }

            // Apply minimum floor
            if (atrStopLossPercent < config.MinimumPercent || result.ATRValue <= 0)
            {
                result.StopLossPercent = config.MinimumPercent;
                result.StopLossPrice = CalculateFixedStopLoss(currentPrice, config.MinimumPercent, isLong);
                result.IsMinimumApplied = true;

                _loggingService.Debug($"[{pair}] ATR stop ({atrStopLossPercent:0.00}%) below minimum ({config.MinimumPercent:0.00}%), using minimum");
            }
            else
            {
                result.StopLossPercent = atrStopLossPercent;
                result.StopLossPrice = atrStopLossPrice;
                result.IsMinimumApplied = false;
            }

            result.IsTriggered = IsStopLossTriggered(currentPrice, result, isLong);

            _loggingService.Info($"[{pair}] Dynamic stop-loss calculated: {result.StopLossPrice:0.00000000} ({result.StopLossPercent:0.00}%), ATR: {result.ATRValue:0.00000000}, Multiplier: {result.ATRMultiplier:0.0}x, Trending: {result.IsTrending}");

            _pairStates[pair] = result;
            return result;
        }

        /// <inheritdoc />
        public StopLossResult UpdateTrailingStop(
            string pair,
            decimal currentPrice,
            StopLossResult previousResult,
            IEnumerable<Candle> candles,
            IStopLossConfig config,
            bool isLong = true)
        {
            if (previousResult == null)
            {
                // No previous state, create new calculation
                return CalculateStopLoss(pair, currentPrice, currentPrice, candles, config, isLong);
            }

            var result = new StopLossResult
            {
                Pair = pair,
                Type = previousResult.Type,
                ATRValue = previousResult.ATRValue,
                ATRMultiplier = previousResult.ATRMultiplier,
                IsTrending = previousResult.IsTrending,
                HighestPrice = previousResult.HighestPrice,
                LowestPrice = previousResult.LowestPrice
            };

            bool shouldRecalculate = false;

            if (isLong)
            {
                // For long positions: trailing stop moves up when price makes new highs
                if (currentPrice > previousResult.HighestPrice)
                {
                    result.HighestPrice = currentPrice;
                    shouldRecalculate = true;
                    _loggingService.Debug($"[{pair}] New high: {currentPrice:0.00000000}, updating trailing stop");
                }
            }
            else
            {
                // For short positions: trailing stop moves down when price makes new lows
                if (currentPrice < previousResult.LowestPrice)
                {
                    result.LowestPrice = currentPrice;
                    shouldRecalculate = true;
                    _loggingService.Debug($"[{pair}] New low: {currentPrice:0.00000000}, updating trailing stop");
                }
            }

            if (shouldRecalculate)
            {
                // Recalculate ATR periodically or when trailing
                var candleList = candles?.ToList() ?? new List<Candle>();
                if (candleList.Count > 0)
                {
                    result.ATRValue = _atrCalculator.CalculateATR(candleList, config.ATRPeriod);
                    result.IsTrending = _atrCalculator.IsTrending(candleList);
                    result.ATRMultiplier = GetRecommendedMultiplier(candleList, config);
                }

                // Calculate new stop based on reference price (highest for long, lowest for short)
                decimal referencePrice = isLong ? result.HighestPrice : result.LowestPrice;

                if (config.Type == StopLossType.Fixed || result.ATRValue <= 0)
                {
                    result.StopLossPercent = config.MinimumPercent;
                    result.StopLossPrice = CalculateFixedStopLoss(referencePrice, config.MinimumPercent, isLong);
                    result.IsMinimumApplied = true;
                }
                else
                {
                    decimal atrStopLossPercent = _atrCalculator.CalculateStopLossPercent(referencePrice, result.ATRValue, result.ATRMultiplier);
                    decimal atrStopLossPrice = _atrCalculator.CalculateStopLoss(referencePrice, result.ATRValue, result.ATRMultiplier, isLong);

                    if (atrStopLossPercent < config.MinimumPercent)
                    {
                        result.StopLossPercent = config.MinimumPercent;
                        result.StopLossPrice = CalculateFixedStopLoss(referencePrice, config.MinimumPercent, isLong);
                        result.IsMinimumApplied = true;
                    }
                    else
                    {
                        result.StopLossPercent = atrStopLossPercent;
                        result.StopLossPrice = atrStopLossPrice;
                        result.IsMinimumApplied = false;
                    }
                }

                // Trailing stop should only move in favorable direction
                if (isLong)
                {
                    // For longs, stop should only move up
                    if (result.StopLossPrice < previousResult.StopLossPrice)
                    {
                        result.StopLossPrice = previousResult.StopLossPrice;
                        result.StopLossPercent = previousResult.StopLossPercent;
                        result.IsMinimumApplied = previousResult.IsMinimumApplied;
                    }
                }
                else
                {
                    // For shorts, stop should only move down
                    if (result.StopLossPrice > previousResult.StopLossPrice)
                    {
                        result.StopLossPrice = previousResult.StopLossPrice;
                        result.StopLossPercent = previousResult.StopLossPercent;
                        result.IsMinimumApplied = previousResult.IsMinimumApplied;
                    }
                }
            }
            else
            {
                // No new extreme, keep previous stop-loss values
                result.StopLossPrice = previousResult.StopLossPrice;
                result.StopLossPercent = previousResult.StopLossPercent;
                result.IsMinimumApplied = previousResult.IsMinimumApplied;
            }

            result.IsTriggered = IsStopLossTriggered(currentPrice, result, isLong);

            if (result.IsTriggered)
            {
                _loggingService.Info($"[{pair}] STOP-LOSS TRIGGERED! Current: {currentPrice:0.00000000}, Stop: {result.StopLossPrice:0.00000000}");
            }

            _pairStates[pair] = result;
            return result;
        }

        /// <inheritdoc />
        public bool IsStopLossTriggered(decimal currentPrice, StopLossResult stopLossResult, bool isLong = true)
        {
            if (stopLossResult == null || stopLossResult.StopLossPrice <= 0)
            {
                return false;
            }

            return isLong
                ? currentPrice <= stopLossResult.StopLossPrice
                : currentPrice >= stopLossResult.StopLossPrice;
        }

        /// <inheritdoc />
        public decimal GetRecommendedMultiplier(IEnumerable<Candle> candles, IStopLossConfig config)
        {
            if (config == null || !config.AutoAdjustMultiplier)
            {
                return config?.ATRMultiplier ?? StopLossConfig.DefaultATRMultiplier;
            }

            bool isTrending = _atrCalculator.IsTrending(candles);

            return isTrending ? config.TrendingMultiplier : config.RangingMultiplier;
        }

        /// <inheritdoc />
        public void ClearPairState(string pair)
        {
            if (!string.IsNullOrEmpty(pair))
            {
                _pairStates.TryRemove(pair, out _);
                _loggingService.Debug($"[{pair}] Stop-loss state cleared");
            }
        }

        /// <inheritdoc />
        public StopLossResult? GetCurrentState(string pair)
        {
            if (string.IsNullOrEmpty(pair))
            {
                return null;
            }

            return _pairStates.TryGetValue(pair, out var result) ? result : null;
        }

        /// <summary>
        /// Calculates a fixed percentage stop-loss price.
        /// </summary>
        private decimal CalculateFixedStopLoss(decimal referencePrice, decimal percentFromPrice, bool isLong)
        {
            if (referencePrice <= 0)
            {
                return 0m;
            }

            decimal distance = referencePrice * (percentFromPrice / 100m);

            return isLong
                ? referencePrice - distance
                : referencePrice + distance;
        }
    }
}
