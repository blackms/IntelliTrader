using System;
using System.Collections.Generic;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Manages portfolio-level risk including heat tracking, circuit breakers, and daily loss limits.
    /// </summary>
    public interface IPortfolioRiskManager
    {
        /// <summary>
        /// Gets the current portfolio heat as a percentage of capital at risk.
        /// </summary>
        /// <returns>Total percentage of capital currently at risk across all positions.</returns>
        decimal GetCurrentHeat();

        /// <summary>
        /// Determines if a new position can be opened given the additional risk.
        /// </summary>
        /// <param name="additionalRisk">The risk amount (as percentage of capital) for the new position.</param>
        /// <returns>True if the position can be opened without exceeding risk limits.</returns>
        bool CanOpenPosition(decimal additionalRisk);

        /// <summary>
        /// Registers a new position with its associated risk amount.
        /// </summary>
        /// <param name="pair">The trading pair identifier.</param>
        /// <param name="riskAmount">The risk amount as percentage of capital.</param>
        void RegisterPosition(string pair, decimal riskAmount);

        /// <summary>
        /// Closes a position and removes it from risk tracking.
        /// </summary>
        /// <param name="pair">The trading pair identifier to close.</param>
        void ClosePosition(string pair);

        /// <summary>
        /// Checks if the circuit breaker has been triggered due to excessive drawdown.
        /// </summary>
        /// <returns>True if circuit breaker is active and trading should be halted.</returns>
        bool IsCircuitBreakerTriggered();

        /// <summary>
        /// Gets the current drawdown percentage from peak equity.
        /// </summary>
        /// <returns>Current drawdown as a positive percentage.</returns>
        decimal GetCurrentDrawdown();

        /// <summary>
        /// Gets the daily profit/loss as percentage of starting daily balance.
        /// </summary>
        /// <returns>Daily P/L percentage (negative for loss).</returns>
        decimal GetDailyProfitLoss();

        /// <summary>
        /// Checks if the daily loss limit has been reached.
        /// </summary>
        /// <returns>True if daily loss limit is exceeded.</returns>
        bool IsDailyLossLimitReached();

        /// <summary>
        /// Gets detailed risk metrics for monitoring.
        /// </summary>
        /// <returns>Dictionary of risk metrics.</returns>
        IDictionary<string, decimal> GetRiskMetrics();

        /// <summary>
        /// Resets the circuit breaker manually (use with caution).
        /// </summary>
        void ResetCircuitBreaker();

        /// <summary>
        /// Updates the peak equity for drawdown calculation.
        /// Should be called when equity reaches a new high.
        /// </summary>
        /// <param name="currentEquity">The current total equity.</param>
        void UpdatePeakEquity(decimal currentEquity);

        /// <summary>
        /// Records a realized profit or loss for daily tracking.
        /// </summary>
        /// <param name="profitLoss">The profit (positive) or loss (negative) amount.</param>
        void RecordTrade(decimal profitLoss);
    }
}
