namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration interface for portfolio risk management settings.
    /// </summary>
    public interface IRiskManagementConfig
    {
        /// <summary>
        /// Whether risk management is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Maximum portfolio heat percentage (total capital at risk).
        /// Default: 6.0%
        /// </summary>
        decimal MaxPortfolioHeat { get; }

        /// <summary>
        /// Maximum drawdown percentage before circuit breaker triggers.
        /// Default: 10.0%
        /// </summary>
        decimal MaxDrawdownPercent { get; }

        /// <summary>
        /// Daily loss limit as percentage of starting daily balance.
        /// Default: 5.0%
        /// </summary>
        decimal DailyLossLimitPercent { get; }

        /// <summary>
        /// Whether the circuit breaker feature is enabled.
        /// </summary>
        bool CircuitBreakerEnabled { get; }

        /// <summary>
        /// Default risk per position as percentage of capital.
        /// Used when calculating position risk for heat tracking.
        /// Default: 1.0%
        /// </summary>
        decimal DefaultPositionRiskPercent { get; }
    }
}
