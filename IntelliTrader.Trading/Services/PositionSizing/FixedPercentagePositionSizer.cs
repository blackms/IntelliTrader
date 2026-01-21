using IntelliTrader.Core;
using System;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Fixed percentage position sizing algorithm.
    ///
    /// Calculates position size based on a fixed percentage of account balance at risk.
    /// Formula: Position Size = (Account Balance x Risk %) / Stop Loss Distance
    ///
    /// Example:
    /// - Account Balance: $100,000
    /// - Risk Percent: 1%
    /// - Entry Price: $50,000
    /// - Stop Loss: $47,500 (5% below entry)
    /// - Stop Loss Distance: $2,500 per unit
    /// - Risk Amount: $100,000 x 1% = $1,000
    /// - Position Size: $1,000 / $2,500 = 0.4 units
    /// - Position Value: 0.4 x $50,000 = $20,000 (20% of account)
    /// </summary>
    public class FixedPercentagePositionSizer : IPositionSizer
    {
        private readonly ILoggingService _loggingService;

        public string Name => "FixedPercent";

        public FixedPercentagePositionSizer(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public decimal CalculatePositionSize(PositionSizingContext context)
        {
            return CalculatePositionSizeWithDetails(context).PositionSize;
        }

        public PositionSizingResult CalculatePositionSizeWithDetails(PositionSizingContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Calculate the amount at risk (Account Balance x Risk %)
            decimal riskAmount = context.AccountBalance * context.RiskPercent;

            // Calculate stop loss distance (absolute difference between entry and stop loss)
            decimal stopLossDistance = Math.Abs(context.EntryPrice - context.StopLossPrice);

            decimal positionSize;
            bool wasCapped = false;

            if (stopLossDistance <= 0)
            {
                // If no stop loss distance, fall back to simple percentage of balance
                _loggingService.Debug($"[{Name}] Stop loss distance is zero, using risk amount directly. Pair: {context.Pair}");
                positionSize = riskAmount;
            }
            else
            {
                // Calculate position size based on risk per unit
                // Risk per unit = stop loss distance relative to entry price
                decimal riskPerUnit = stopLossDistance;

                // Position size in units
                decimal positionSizeUnits = riskAmount / riskPerUnit;

                // Convert to quote currency value
                positionSize = positionSizeUnits * context.EntryPrice;
            }

            // Apply maximum position size cap
            decimal maxPositionSize = context.AccountBalance * context.MaxPositionPercent;
            if (positionSize > maxPositionSize)
            {
                _loggingService.Debug($"[{Name}] Position size capped from {positionSize:0.00000000} to {maxPositionSize:0.00000000}. Pair: {context.Pair}");
                positionSize = maxPositionSize;
                wasCapped = true;
            }

            // Ensure position size is not negative
            positionSize = Math.Max(0, positionSize);

            // Calculate position as percentage of account
            decimal positionPercent = context.AccountBalance > 0
                ? positionSize / context.AccountBalance
                : 0;

            _loggingService.Debug($"[{Name}] Calculated position size: {positionSize:0.00000000}, " +
                $"Risk: {riskAmount:0.00000000}, Position%: {positionPercent * 100:0.00}%, " +
                $"Capped: {wasCapped}. Pair: {context.Pair}");

            return new PositionSizingResult(
                positionSize: positionSize,
                riskAmount: riskAmount,
                positionPercent: positionPercent,
                wasCapped: wasCapped,
                method: Name
            );
        }
    }
}
