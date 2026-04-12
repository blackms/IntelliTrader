using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for sell order parameters including margin targets, trailing, and stop-loss.
    /// </summary>
    public interface ISellConfig
    {
        /// <summary>
        /// Whether selling is enabled.
        /// </summary>
        bool SellEnabled { get; set; }

        /// <summary>
        /// The order type for sell orders (e.g., Market, Limit).
        /// </summary>
        OrderType SellType { get; }

        /// <summary>
        /// Target profit margin percentage to trigger a sell.
        /// </summary>
        decimal SellMargin { get; }

        /// <summary>
        /// Trailing percentage for sell orders (price must drop this much from peak before selling).
        /// </summary>
        decimal SellTrailing { get; }

        /// <summary>
        /// Stop margin for trailing sell - triggers sell when margin drops below this from trailing high.
        /// </summary>
        decimal SellTrailingStopMargin { get; }

        /// <summary>
        /// Action to take when the trailing sell stop margin is reached.
        /// </summary>
        SellTrailingStopAction SellTrailingStopAction { get; }

        /// <summary>
        /// Whether stop-loss selling is enabled.
        /// </summary>
        bool SellStopLossEnabled { get; }

        /// <summary>
        /// Whether stop-loss only activates after at least one DCA level has been executed.
        /// </summary>
        bool SellStopLossAfterDCA { get; }

        /// <summary>
        /// Minimum position age in minutes before stop-loss can trigger.
        /// </summary>
        double SellStopLossMinAge { get; }

        /// <summary>
        /// The stop-loss margin percentage (negative value indicating loss threshold).
        /// </summary>
        decimal SellStopLossMargin { get; }

        /// <summary>
        /// Configuration for dynamic ATR-based stop-loss.
        /// When StopLoss.Type is ATR, the SellStopLossMargin becomes a fallback minimum.
        /// </summary>
        IStopLossConfig StopLoss { get; }
    }
}
