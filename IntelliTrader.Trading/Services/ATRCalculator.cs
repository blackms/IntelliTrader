using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Implementation of Average True Range (ATR) calculations for volatility-based stop-loss management.
    /// </summary>
    internal class ATRCalculator : IATRCalculator
    {
        private const int MinimumCandlesRequired = 2;
        private const decimal TrendingThreshold = 1.2m; // Short ATR > Long ATR * 1.2 indicates trending

        private readonly ILoggingService _loggingService;

        public ATRCalculator(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <inheritdoc />
        public decimal CalculateATR(IEnumerable<Candle> candles, int period = 14)
        {
            if (candles == null)
            {
                _loggingService.Debug("ATR calculation: No candles provided");
                return 0m;
            }

            var candleList = candles.OrderBy(c => c.Timestamp).ToList();

            if (candleList.Count < MinimumCandlesRequired)
            {
                _loggingService.Debug($"ATR calculation: Insufficient candles ({candleList.Count}), need at least {MinimumCandlesRequired}");
                return 0m;
            }

            // Calculate True Range for each candle
            var trueRanges = new List<decimal>();

            for (int i = 0; i < candleList.Count; i++)
            {
                if (i == 0)
                {
                    // First candle: use simple High - Low
                    trueRanges.Add(candleList[i].CalculateTrueRange());
                }
                else
                {
                    // Subsequent candles: use full True Range formula
                    trueRanges.Add(candleList[i].CalculateTrueRange(candleList[i - 1].Close));
                }
            }

            // If we have fewer candles than the period, use all available
            int effectivePeriod = Math.Min(period, trueRanges.Count);

            if (effectivePeriod == 0)
            {
                return 0m;
            }

            // Use Wilder's smoothing method (exponential moving average)
            // First ATR = Simple average of first 'period' True Ranges
            // Subsequent ATR = ((Prior ATR * (period - 1)) + Current TR) / period

            var recentTrueRanges = trueRanges.Skip(trueRanges.Count - effectivePeriod).ToList();

            if (recentTrueRanges.Count <= effectivePeriod)
            {
                // Not enough data for Wilder's smoothing, use simple average
                return recentTrueRanges.Average();
            }

            // Calculate using Wilder's smoothing
            decimal atr = recentTrueRanges.Take(effectivePeriod).Average();

            for (int i = effectivePeriod; i < recentTrueRanges.Count; i++)
            {
                atr = ((atr * (effectivePeriod - 1)) + recentTrueRanges[i]) / effectivePeriod;
            }

            _loggingService.Debug($"ATR calculated: {atr:0.00000000} (period: {effectivePeriod}, candles: {candleList.Count})");
            return atr;
        }

        /// <inheritdoc />
        public decimal CalculateStopLoss(decimal referencePrice, decimal atr, decimal multiplier, bool isLong)
        {
            if (referencePrice <= 0)
            {
                _loggingService.Debug("Stop-loss calculation: Invalid reference price");
                return 0m;
            }

            if (atr <= 0)
            {
                _loggingService.Debug("Stop-loss calculation: Invalid ATR value");
                return referencePrice; // Return reference price as fallback
            }

            decimal atrDistance = atr * multiplier;

            decimal stopLoss = isLong
                ? referencePrice - atrDistance  // Long: stop below price
                : referencePrice + atrDistance; // Short: stop above price

            // Ensure stop-loss is positive
            stopLoss = Math.Max(0, stopLoss);

            _loggingService.Debug($"Stop-loss calculated: {stopLoss:0.00000000} (ref: {referencePrice:0.00000000}, ATR: {atr:0.00000000}, mult: {multiplier}, isLong: {isLong})");
            return stopLoss;
        }

        /// <inheritdoc />
        public decimal CalculateStopLossPercent(decimal currentPrice, decimal atr, decimal multiplier)
        {
            if (currentPrice <= 0)
            {
                return 0m;
            }

            if (atr <= 0)
            {
                return 0m;
            }

            decimal atrDistance = atr * multiplier;
            decimal percent = (atrDistance / currentPrice) * 100m;

            _loggingService.Debug($"Stop-loss percent calculated: {percent:0.00}% (price: {currentPrice:0.00000000}, ATR: {atr:0.00000000}, mult: {multiplier})");
            return percent;
        }

        /// <inheritdoc />
        public bool IsTrending(IEnumerable<Candle> candles, int shortPeriod = 7, int longPeriod = 14)
        {
            if (candles == null)
            {
                return false;
            }

            var candleList = candles.ToList();

            if (candleList.Count < longPeriod)
            {
                // Not enough data to determine trend, assume ranging (more conservative)
                return false;
            }

            decimal shortATR = CalculateATR(candleList, shortPeriod);
            decimal longATR = CalculateATR(candleList, longPeriod);

            if (longATR <= 0)
            {
                return false;
            }

            // If short-term volatility is significantly higher than long-term, market is trending
            bool isTrending = shortATR > (longATR * TrendingThreshold);

            _loggingService.Debug($"Trend detection: {(isTrending ? "Trending" : "Ranging")} (Short ATR: {shortATR:0.00000000}, Long ATR: {longATR:0.00000000})");
            return isTrending;
        }
    }
}
