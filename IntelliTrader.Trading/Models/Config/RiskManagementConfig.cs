using IntelliTrader.Core;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Configuration model for portfolio risk management settings.
    /// </summary>
    internal class RiskManagementConfig : IRiskManagementConfig
    {
        /// <inheritdoc />
        public bool Enabled { get; set; } = true;

        /// <inheritdoc />
        public decimal MaxPortfolioHeat { get; set; } = 6.0m;

        /// <inheritdoc />
        public decimal MaxDrawdownPercent { get; set; } = 10.0m;

        /// <inheritdoc />
        public decimal DailyLossLimitPercent { get; set; } = 5.0m;

        /// <inheritdoc />
        public bool CircuitBreakerEnabled { get; set; } = true;

        /// <inheritdoc />
        public decimal DefaultPositionRiskPercent { get; set; } = 1.0m;
    }
}
