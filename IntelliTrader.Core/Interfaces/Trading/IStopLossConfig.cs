namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration interface for stop-loss settings.
    /// </summary>
    public interface IStopLossConfig
    {
        /// <summary>
        /// The type of stop-loss calculation to use (ATR or Fixed).
        /// Default: Fixed (for backward compatibility).
        /// </summary>
        StopLossType Type { get; }

        /// <summary>
        /// The ATR period for calculating Average True Range.
        /// Typically 14 periods (e.g., 14 candles).
        /// Default: 14.
        /// </summary>
        int ATRPeriod { get; }

        /// <summary>
        /// The ATR multiplier for calculating stop distance.
        /// Typical values: 2-3x for ranging markets, 3-4x for trending markets.
        /// Default: 3.0.
        /// </summary>
        decimal ATRMultiplier { get; }

        /// <summary>
        /// The minimum stop-loss percentage (floor) regardless of ATR calculation.
        /// Ensures stops are never too tight, protecting against flash crashes.
        /// Default: 2.0 (2%).
        /// </summary>
        decimal MinimumPercent { get; }

        /// <summary>
        /// The ATR multiplier to use in ranging market conditions.
        /// Lower multiplier = tighter stops for ranging markets.
        /// Default: 2.5.
        /// </summary>
        decimal RangingMultiplier { get; }

        /// <summary>
        /// The ATR multiplier to use in trending market conditions.
        /// Higher multiplier = wider stops to ride trends.
        /// Default: 3.5.
        /// </summary>
        decimal TrendingMultiplier { get; }

        /// <summary>
        /// Whether to automatically adjust multiplier based on market conditions.
        /// When true, uses RangingMultiplier or TrendingMultiplier based on market state.
        /// When false, always uses ATRMultiplier.
        /// Default: true.
        /// </summary>
        bool AutoAdjustMultiplier { get; }
    }
}
