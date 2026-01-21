using IntelliTrader.Core;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Configuration class for stop-loss settings.
    /// </summary>
    public class StopLossConfig : IStopLossConfig
    {
        /// <summary>
        /// Default ATR period (14 candles).
        /// </summary>
        public const int DefaultATRPeriod = 14;

        /// <summary>
        /// Default ATR multiplier.
        /// </summary>
        public const decimal DefaultATRMultiplier = 3.0m;

        /// <summary>
        /// Default minimum stop-loss percentage.
        /// </summary>
        public const decimal DefaultMinimumPercent = 2.0m;

        /// <summary>
        /// Default multiplier for ranging markets.
        /// </summary>
        public const decimal DefaultRangingMultiplier = 2.5m;

        /// <summary>
        /// Default multiplier for trending markets.
        /// </summary>
        public const decimal DefaultTrendingMultiplier = 3.5m;

        /// <inheritdoc />
        public StopLossType Type { get; set; } = StopLossType.Fixed;

        /// <inheritdoc />
        public int ATRPeriod { get; set; } = DefaultATRPeriod;

        /// <inheritdoc />
        public decimal ATRMultiplier { get; set; } = DefaultATRMultiplier;

        /// <inheritdoc />
        public decimal MinimumPercent { get; set; } = DefaultMinimumPercent;

        /// <inheritdoc />
        public decimal RangingMultiplier { get; set; } = DefaultRangingMultiplier;

        /// <inheritdoc />
        public decimal TrendingMultiplier { get; set; } = DefaultTrendingMultiplier;

        /// <inheritdoc />
        public bool AutoAdjustMultiplier { get; set; } = true;

        /// <summary>
        /// Creates a clone of this configuration.
        /// </summary>
        /// <returns>A new StopLossConfig with the same settings.</returns>
        public StopLossConfig Clone()
        {
            return new StopLossConfig
            {
                Type = Type,
                ATRPeriod = ATRPeriod,
                ATRMultiplier = ATRMultiplier,
                MinimumPercent = MinimumPercent,
                RangingMultiplier = RangingMultiplier,
                TrendingMultiplier = TrendingMultiplier,
                AutoAdjustMultiplier = AutoAdjustMultiplier
            };
        }

        /// <summary>
        /// Creates a default ATR-based stop-loss configuration.
        /// </summary>
        /// <returns>A StopLossConfig configured for ATR-based stops.</returns>
        public static StopLossConfig CreateATRDefault()
        {
            return new StopLossConfig
            {
                Type = StopLossType.ATR,
                ATRPeriod = DefaultATRPeriod,
                ATRMultiplier = DefaultATRMultiplier,
                MinimumPercent = DefaultMinimumPercent,
                RangingMultiplier = DefaultRangingMultiplier,
                TrendingMultiplier = DefaultTrendingMultiplier,
                AutoAdjustMultiplier = true
            };
        }

        /// <summary>
        /// Creates a fixed percentage stop-loss configuration (legacy behavior).
        /// </summary>
        /// <param name="minimumPercent">The fixed stop-loss percentage.</param>
        /// <returns>A StopLossConfig configured for fixed stops.</returns>
        public static StopLossConfig CreateFixedDefault(decimal minimumPercent = DefaultMinimumPercent)
        {
            return new StopLossConfig
            {
                Type = StopLossType.Fixed,
                MinimumPercent = minimumPercent,
                AutoAdjustMultiplier = false
            };
        }
    }
}
