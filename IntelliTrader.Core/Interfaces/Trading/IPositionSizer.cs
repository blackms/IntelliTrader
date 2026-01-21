using System;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Context containing all information needed for position size calculation.
    /// </summary>
    public class PositionSizingContext
    {
        /// <summary>
        /// Current account balance available for trading.
        /// </summary>
        public decimal AccountBalance { get; }

        /// <summary>
        /// Risk percentage per trade (e.g., 0.01 = 1%).
        /// </summary>
        public decimal RiskPercent { get; }

        /// <summary>
        /// Expected entry price for the trade.
        /// </summary>
        public decimal EntryPrice { get; }

        /// <summary>
        /// Stop loss price level. Used to calculate risk per unit.
        /// </summary>
        public decimal StopLossPrice { get; }

        /// <summary>
        /// Historical win rate (probability of winning trade).
        /// Required for Kelly Criterion calculation.
        /// </summary>
        public decimal? WinRate { get; }

        /// <summary>
        /// Average win/loss ratio (average win amount / average loss amount).
        /// Required for Kelly Criterion calculation.
        /// </summary>
        public decimal? AverageWinLoss { get; }

        /// <summary>
        /// Maximum allowed position size as percentage of account (e.g., 0.10 = 10%).
        /// </summary>
        public decimal MaxPositionPercent { get; }

        /// <summary>
        /// Trading pair identifier.
        /// </summary>
        public string Pair { get; }

        public PositionSizingContext(
            decimal accountBalance,
            decimal riskPercent,
            decimal entryPrice,
            decimal stopLossPrice,
            decimal? winRate = null,
            decimal? averageWinLoss = null,
            decimal maxPositionPercent = 0.10m,
            string pair = null)
        {
            if (accountBalance <= 0)
                throw new ArgumentException("Account balance must be positive", nameof(accountBalance));
            if (riskPercent <= 0 || riskPercent > 1)
                throw new ArgumentException("Risk percent must be between 0 and 1", nameof(riskPercent));
            if (entryPrice <= 0)
                throw new ArgumentException("Entry price must be positive", nameof(entryPrice));
            if (maxPositionPercent <= 0 || maxPositionPercent > 1)
                throw new ArgumentException("Max position percent must be between 0 and 1", nameof(maxPositionPercent));

            AccountBalance = accountBalance;
            RiskPercent = riskPercent;
            EntryPrice = entryPrice;
            StopLossPrice = stopLossPrice;
            WinRate = winRate;
            AverageWinLoss = averageWinLoss;
            MaxPositionPercent = maxPositionPercent;
            Pair = pair;
        }
    }

    /// <summary>
    /// Result of position size calculation containing the recommended position size
    /// and additional metadata about the calculation.
    /// </summary>
    public class PositionSizingResult
    {
        /// <summary>
        /// Recommended position size in quote currency (e.g., BTC).
        /// </summary>
        public decimal PositionSize { get; }

        /// <summary>
        /// Dollar amount at risk based on stop loss distance.
        /// </summary>
        public decimal RiskAmount { get; }

        /// <summary>
        /// Position size as percentage of account balance.
        /// </summary>
        public decimal PositionPercent { get; }

        /// <summary>
        /// Indicates if the position was capped by MaxPositionPercent.
        /// </summary>
        public bool WasCapped { get; }

        /// <summary>
        /// The sizing method used (e.g., "FixedPercent", "Kelly").
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Kelly percentage before fractional adjustment (only for Kelly method).
        /// </summary>
        public decimal? RawKellyPercent { get; }

        public PositionSizingResult(
            decimal positionSize,
            decimal riskAmount,
            decimal positionPercent,
            bool wasCapped,
            string method,
            decimal? rawKellyPercent = null)
        {
            PositionSize = positionSize;
            RiskAmount = riskAmount;
            PositionPercent = positionPercent;
            WasCapped = wasCapped;
            Method = method;
            RawKellyPercent = rawKellyPercent;
        }
    }

    /// <summary>
    /// Interface for position sizing algorithms that determine optimal trade size
    /// based on account balance, risk parameters, and trading statistics.
    /// </summary>
    public interface IPositionSizer
    {
        /// <summary>
        /// Gets the name of this position sizing strategy.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Calculates the recommended position size based on the provided context.
        /// </summary>
        /// <param name="context">Context containing account and risk parameters.</param>
        /// <returns>The calculated position size in quote currency.</returns>
        decimal CalculatePositionSize(PositionSizingContext context);

        /// <summary>
        /// Calculates the recommended position size with detailed result information.
        /// </summary>
        /// <param name="context">Context containing account and risk parameters.</param>
        /// <returns>Detailed result including position size and calculation metadata.</returns>
        PositionSizingResult CalculatePositionSizeWithDetails(PositionSizingContext context);
    }
}
