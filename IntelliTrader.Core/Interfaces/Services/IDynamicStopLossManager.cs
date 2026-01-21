using System.Collections.Generic;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents the result of a stop-loss calculation.
    /// </summary>
    public class StopLossResult
    {
        /// <summary>
        /// The trading pair this stop-loss applies to.
        /// </summary>
        public string Pair { get; set; } = string.Empty;

        /// <summary>
        /// The calculated stop-loss price level.
        /// </summary>
        public decimal StopLossPrice { get; set; }

        /// <summary>
        /// The stop-loss as a percentage from reference price.
        /// </summary>
        public decimal StopLossPercent { get; set; }

        /// <summary>
        /// The ATR value used for calculation.
        /// </summary>
        public decimal ATRValue { get; set; }

        /// <summary>
        /// The ATR multiplier used.
        /// </summary>
        public decimal ATRMultiplier { get; set; }

        /// <summary>
        /// Whether the stop-loss was capped at minimum percentage.
        /// </summary>
        public bool IsMinimumApplied { get; set; }

        /// <summary>
        /// The highest price seen since entry (for trailing long stops).
        /// </summary>
        public decimal HighestPrice { get; set; }

        /// <summary>
        /// The lowest price seen since entry (for trailing short stops).
        /// </summary>
        public decimal LowestPrice { get; set; }

        /// <summary>
        /// Whether the current price has triggered the stop-loss.
        /// </summary>
        public bool IsTriggered { get; set; }

        /// <summary>
        /// Whether the market is detected as trending.
        /// </summary>
        public bool IsTrending { get; set; }

        /// <summary>
        /// The type of stop-loss applied (ATR or Fixed).
        /// </summary>
        public StopLossType Type { get; set; }
    }

    /// <summary>
    /// Manages dynamic ATR-based trailing stop-loss calculations for trading pairs.
    /// </summary>
    public interface IDynamicStopLossManager
    {
        /// <summary>
        /// Calculates the dynamic stop-loss for a trading pair based on ATR.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <param name="entryPrice">The average entry price.</param>
        /// <param name="currentPrice">The current market price.</param>
        /// <param name="candles">Recent price candles for ATR calculation.</param>
        /// <param name="config">The stop-loss configuration.</param>
        /// <param name="isLong">True for long positions (default), false for short.</param>
        /// <returns>The stop-loss calculation result.</returns>
        StopLossResult CalculateStopLoss(
            string pair,
            decimal entryPrice,
            decimal currentPrice,
            IEnumerable<Candle> candles,
            IStopLossConfig config,
            bool isLong = true);

        /// <summary>
        /// Updates the trailing stop-loss based on price movement.
        /// For long positions: stop moves up when price makes new highs.
        /// For short positions: stop moves down when price makes new lows.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <param name="currentPrice">The current market price.</param>
        /// <param name="previousResult">The previous stop-loss calculation result.</param>
        /// <param name="candles">Recent price candles for ATR calculation.</param>
        /// <param name="config">The stop-loss configuration.</param>
        /// <param name="isLong">True for long positions (default), false for short.</param>
        /// <returns>The updated stop-loss calculation result.</returns>
        StopLossResult UpdateTrailingStop(
            string pair,
            decimal currentPrice,
            StopLossResult previousResult,
            IEnumerable<Candle> candles,
            IStopLossConfig config,
            bool isLong = true);

        /// <summary>
        /// Checks if the stop-loss has been triggered.
        /// </summary>
        /// <param name="currentPrice">The current market price.</param>
        /// <param name="stopLossResult">The stop-loss calculation result.</param>
        /// <param name="isLong">True for long positions (default), false for short.</param>
        /// <returns>True if stop-loss is triggered.</returns>
        bool IsStopLossTriggered(decimal currentPrice, StopLossResult stopLossResult, bool isLong = true);

        /// <summary>
        /// Gets the recommended ATR multiplier based on market conditions.
        /// Returns higher multiplier (3-4x) for trending markets, lower (2-3x) for ranging.
        /// </summary>
        /// <param name="candles">Recent price candles for trend detection.</param>
        /// <param name="config">The stop-loss configuration.</param>
        /// <returns>The recommended ATR multiplier.</returns>
        decimal GetRecommendedMultiplier(IEnumerable<Candle> candles, IStopLossConfig config);

        /// <summary>
        /// Clears any cached stop-loss state for a pair (call when position is closed).
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        void ClearPairState(string pair);

        /// <summary>
        /// Gets the current stop-loss state for a pair, if any.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The current stop-loss result, or null if none exists.</returns>
        StopLossResult? GetCurrentState(string pair);
    }
}
