using System.Collections.Generic;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for calculating Average True Range (ATR) and ATR-based stop-loss levels.
    /// </summary>
    public interface IATRCalculator
    {
        /// <summary>
        /// Calculates the Average True Range (ATR) from a collection of candles.
        /// ATR is a volatility indicator that measures the average range of price movement.
        /// </summary>
        /// <param name="candles">Collection of price candles (must have at least 'period' candles).</param>
        /// <param name="period">The number of periods to use for ATR calculation (default: 14).</param>
        /// <returns>The calculated ATR value, or 0 if insufficient data.</returns>
        decimal CalculateATR(IEnumerable<Candle> candles, int period = 14);

        /// <summary>
        /// Calculates the stop-loss price level based on ATR.
        /// For long positions: Stop = entryPrice - (ATR x multiplier) or HighestPrice - (ATR x multiplier) for trailing
        /// For short positions: Stop = entryPrice + (ATR x multiplier) or LowestPrice + (ATR x multiplier) for trailing
        /// </summary>
        /// <param name="referencePrice">The reference price (entry price, highest/lowest price for trailing).</param>
        /// <param name="atr">The calculated ATR value.</param>
        /// <param name="multiplier">The ATR multiplier (typically 2-4x).</param>
        /// <param name="isLong">True for long positions, false for short positions.</param>
        /// <returns>The calculated stop-loss price level.</returns>
        decimal CalculateStopLoss(decimal referencePrice, decimal atr, decimal multiplier, bool isLong);

        /// <summary>
        /// Calculates the stop-loss percentage based on ATR relative to price.
        /// Useful for comparing with fixed percentage stop-losses.
        /// </summary>
        /// <param name="currentPrice">The current price of the asset.</param>
        /// <param name="atr">The calculated ATR value.</param>
        /// <param name="multiplier">The ATR multiplier (typically 2-4x).</param>
        /// <returns>The stop-loss as a percentage (e.g., 2.5 for 2.5%).</returns>
        decimal CalculateStopLossPercent(decimal currentPrice, decimal atr, decimal multiplier);

        /// <summary>
        /// Determines if the market is trending based on ATR analysis.
        /// Uses ATR comparison over different periods to detect trends.
        /// </summary>
        /// <param name="candles">Collection of price candles.</param>
        /// <param name="shortPeriod">Short-term ATR period (default: 7).</param>
        /// <param name="longPeriod">Long-term ATR period (default: 14).</param>
        /// <returns>True if market appears to be trending (higher volatility), false for ranging.</returns>
        bool IsTrending(IEnumerable<Candle> candles, int shortPeriod = 7, int longPeriod = 14);
    }
}
