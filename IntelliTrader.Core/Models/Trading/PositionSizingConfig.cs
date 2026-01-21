namespace IntelliTrader.Core
{
    /// <summary>
    /// Position sizing strategy types.
    /// </summary>
    public enum PositionSizingType
    {
        /// <summary>
        /// Fixed percentage of account balance per trade.
        /// Simple and widely used approach.
        /// </summary>
        FixedPercent,

        /// <summary>
        /// Kelly Criterion based sizing using historical win rate and win/loss ratio.
        /// Mathematically optimal for long-term capital growth.
        /// </summary>
        Kelly
    }

    /// <summary>
    /// Configuration for position sizing algorithms.
    /// </summary>
    public class PositionSizingConfig
    {
        /// <summary>
        /// Whether position sizing is enabled.
        /// When disabled, uses BuyMaxCost from trading config.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Type of position sizing algorithm to use.
        /// </summary>
        public PositionSizingType Type { get; set; } = PositionSizingType.FixedPercent;

        /// <summary>
        /// Risk percentage per trade (1.0 = 1% of account).
        /// Used by FixedPercent and as a cap for Kelly.
        /// Recommended: 0.5% to 2% for most traders.
        /// </summary>
        public decimal RiskPercent { get; set; } = 1.0m;

        /// <summary>
        /// Fractional Kelly multiplier (0.0 to 1.0).
        /// Full Kelly (1.0) is aggressive; half Kelly (0.5) is recommended.
        /// </summary>
        public decimal KellyFraction { get; set; } = 0.5m;

        /// <summary>
        /// Maximum position size as percentage of account (10.0 = 10%).
        /// Acts as a safety cap regardless of calculated size.
        /// </summary>
        public decimal MaxPositionPercent { get; set; } = 10.0m;

        /// <summary>
        /// Stop loss percentage below entry price used for risk calculation.
        /// If not using actual stop loss orders, this represents the expected max drawdown.
        /// Example: 5.0 means 5% below entry price.
        /// </summary>
        public decimal DefaultStopLossPercent { get; set; } = 5.0m;

        /// <summary>
        /// Historical win rate for Kelly calculation (0.0 to 1.0).
        /// Example: 0.55 means 55% of trades are winners.
        /// Can be overridden by actual trading statistics.
        /// </summary>
        public decimal? WinRate { get; set; }

        /// <summary>
        /// Average win/loss ratio for Kelly calculation.
        /// Example: 1.5 means average win is 1.5x the average loss.
        /// Can be overridden by actual trading statistics.
        /// </summary>
        public decimal? AverageWinLoss { get; set; }

        /// <summary>
        /// Minimum number of trades required before using historical stats.
        /// Below this threshold, uses configured WinRate and AverageWinLoss.
        /// </summary>
        public int MinTradesForStats { get; set; } = 30;

        /// <summary>
        /// Creates a deep copy of this configuration.
        /// </summary>
        public PositionSizingConfig Clone()
        {
            return new PositionSizingConfig
            {
                Enabled = Enabled,
                Type = Type,
                RiskPercent = RiskPercent,
                KellyFraction = KellyFraction,
                MaxPositionPercent = MaxPositionPercent,
                DefaultStopLossPercent = DefaultStopLossPercent,
                WinRate = WinRate,
                AverageWinLoss = AverageWinLoss,
                MinTradesForStats = MinTradesForStats
            };
        }
    }
}
