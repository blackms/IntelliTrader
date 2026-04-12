using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for buy order parameters including cost limits, trailing, and balance requirements.
    /// </summary>
    public interface IBuyConfig
    {
        /// <summary>
        /// Whether buying is enabled.
        /// </summary>
        bool BuyEnabled { get; set; }

        /// <summary>
        /// The order type for buy orders (e.g., Market, Limit).
        /// </summary>
        OrderType BuyType { get; }

        /// <summary>
        /// Maximum cost per buy order in market currency.
        /// </summary>
        decimal BuyMaxCost { get; }

        /// <summary>
        /// Multiplier applied to the buy cost calculation.
        /// </summary>
        decimal BuyMultiplier { get; }

        /// <summary>
        /// Minimum account balance required to place a buy order.
        /// </summary>
        decimal BuyMinBalance { get; }

        /// <summary>
        /// Timeout in minutes before the same pair can be bought again.
        /// </summary>
        double BuySamePairTimeout { get; }

        /// <summary>
        /// Trailing percentage for buy orders (price must drop this much from peak before buying).
        /// </summary>
        decimal BuyTrailing { get; }

        /// <summary>
        /// Stop margin for trailing buy - maximum allowed price increase from trailing low.
        /// </summary>
        decimal BuyTrailingStopMargin { get; }

        /// <summary>
        /// Action to take when the trailing buy stop margin is reached.
        /// </summary>
        BuyTrailingStopAction BuyTrailingStopAction { get; }
    }
}
