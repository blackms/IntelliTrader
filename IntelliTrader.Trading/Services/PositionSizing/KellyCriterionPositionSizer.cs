using IntelliTrader.Core;
using System;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Kelly Criterion position sizing algorithm.
    ///
    /// The Kelly Criterion is a formula for determining the optimal size of a series of bets
    /// to maximize long-term wealth growth. It was developed by John Kelly at Bell Labs in 1956.
    ///
    /// Formula: Kelly % = W - [(1 - W) / R]
    /// Where:
    ///   W = Win probability (win rate)
    ///   R = Win/Loss ratio (average win / average loss)
    ///
    /// Example:
    /// - Win Rate (W): 55% (0.55)
    /// - Average Win: $150
    /// - Average Loss: $100
    /// - Win/Loss Ratio (R): 1.5
    /// - Kelly % = 0.55 - [(1 - 0.55) / 1.5] = 0.55 - 0.30 = 0.25 (25%)
    ///
    /// Fractional Kelly:
    /// Full Kelly is often too aggressive and can lead to large drawdowns.
    /// Half Kelly (0.5x) is commonly recommended for a balance between growth and risk.
    /// - Full Kelly: 25%
    /// - Half Kelly: 12.5%
    ///
    /// The position size is then capped by MaxPositionPercent for additional safety.
    /// </summary>
    public class KellyCriterionPositionSizer : IPositionSizer
    {
        private readonly ILoggingService _loggingService;
        private readonly decimal _kellyFraction;

        /// <summary>
        /// Default Kelly fraction for safety (0.5 = half Kelly).
        /// </summary>
        public const decimal DefaultKellyFraction = 0.5m;

        public string Name => "Kelly";

        public KellyCriterionPositionSizer(ILoggingService loggingService, decimal kellyFraction = DefaultKellyFraction)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            if (kellyFraction <= 0 || kellyFraction > 1)
                throw new ArgumentException("Kelly fraction must be between 0 and 1", nameof(kellyFraction));

            _kellyFraction = kellyFraction;
        }

        public decimal CalculatePositionSize(PositionSizingContext context)
        {
            return CalculatePositionSizeWithDetails(context).PositionSize;
        }

        public PositionSizingResult CalculatePositionSizeWithDetails(PositionSizingContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Validate required parameters for Kelly calculation
            if (!context.WinRate.HasValue || !context.AverageWinLoss.HasValue)
            {
                _loggingService.Debug($"[{Name}] Missing win rate or average win/loss ratio, " +
                    $"falling back to fixed percentage. Pair: {context.Pair}");

                // Fall back to simple percentage-based sizing
                return CalculateFallbackPositionSize(context);
            }

            decimal winRate = context.WinRate.Value;
            decimal winLossRatio = context.AverageWinLoss.Value;

            // Validate win rate bounds
            if (winRate <= 0 || winRate >= 1)
            {
                _loggingService.Debug($"[{Name}] Invalid win rate {winRate:P2}, " +
                    $"falling back to fixed percentage. Pair: {context.Pair}");
                return CalculateFallbackPositionSize(context);
            }

            // Validate win/loss ratio
            if (winLossRatio <= 0)
            {
                _loggingService.Debug($"[{Name}] Invalid win/loss ratio {winLossRatio:0.00}, " +
                    $"falling back to fixed percentage. Pair: {context.Pair}");
                return CalculateFallbackPositionSize(context);
            }

            // Calculate raw Kelly percentage
            // Kelly % = W - [(1 - W) / R]
            decimal rawKellyPercent = winRate - ((1 - winRate) / winLossRatio);

            // If Kelly is negative, don't trade (edge is negative)
            if (rawKellyPercent <= 0)
            {
                _loggingService.Info($"[{Name}] Negative Kelly ({rawKellyPercent:P2}): negative edge detected. " +
                    $"Win Rate: {winRate:P2}, W/L Ratio: {winLossRatio:0.00}. Pair: {context.Pair}");

                return new PositionSizingResult(
                    positionSize: 0,
                    riskAmount: 0,
                    positionPercent: 0,
                    wasCapped: false,
                    method: Name,
                    rawKellyPercent: rawKellyPercent
                );
            }

            // Apply fractional Kelly for safety
            decimal adjustedKellyPercent = rawKellyPercent * _kellyFraction;

            // Cap by the configured risk percent as well
            adjustedKellyPercent = Math.Min(adjustedKellyPercent, context.RiskPercent * 10);

            // Calculate position size
            decimal positionSize = context.AccountBalance * adjustedKellyPercent;

            // Apply maximum position size cap
            decimal maxPositionSize = context.AccountBalance * context.MaxPositionPercent;
            bool wasCapped = false;

            if (positionSize > maxPositionSize)
            {
                _loggingService.Debug($"[{Name}] Position size capped from {positionSize:0.00000000} to {maxPositionSize:0.00000000}. Pair: {context.Pair}");
                positionSize = maxPositionSize;
                wasCapped = true;
            }

            // Ensure position size is not negative
            positionSize = Math.Max(0, positionSize);

            // Calculate risk amount based on stop loss distance
            decimal stopLossDistance = Math.Abs(context.EntryPrice - context.StopLossPrice);
            decimal riskPercent = context.EntryPrice > 0 ? stopLossDistance / context.EntryPrice : 0;
            decimal riskAmount = positionSize * riskPercent;

            // Calculate position as percentage of account
            decimal positionPercent = context.AccountBalance > 0
                ? positionSize / context.AccountBalance
                : 0;

            _loggingService.Debug($"[{Name}] Raw Kelly: {rawKellyPercent:P2}, Fractional ({_kellyFraction}): {adjustedKellyPercent:P2}, " +
                $"Position: {positionSize:0.00000000} ({positionPercent:P2}), Capped: {wasCapped}. Pair: {context.Pair}");

            return new PositionSizingResult(
                positionSize: positionSize,
                riskAmount: riskAmount,
                positionPercent: positionPercent,
                wasCapped: wasCapped,
                method: Name,
                rawKellyPercent: rawKellyPercent
            );
        }

        private PositionSizingResult CalculateFallbackPositionSize(PositionSizingContext context)
        {
            // Fall back to simple risk-based sizing
            decimal riskAmount = context.AccountBalance * context.RiskPercent;
            decimal positionSize = riskAmount;

            // Apply maximum position size cap
            decimal maxPositionSize = context.AccountBalance * context.MaxPositionPercent;
            bool wasCapped = false;

            if (positionSize > maxPositionSize)
            {
                positionSize = maxPositionSize;
                wasCapped = true;
            }

            decimal positionPercent = context.AccountBalance > 0
                ? positionSize / context.AccountBalance
                : 0;

            return new PositionSizingResult(
                positionSize: positionSize,
                riskAmount: riskAmount,
                positionPercent: positionPercent,
                wasCapped: wasCapped,
                method: $"{Name} (fallback)"
            );
        }
    }
}
