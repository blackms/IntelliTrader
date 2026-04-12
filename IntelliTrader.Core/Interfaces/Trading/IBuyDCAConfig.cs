using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for Dollar Cost Averaging (DCA) buy operations that add to existing positions.
    /// </summary>
    public interface IBuyDCAConfig
    {
        /// <summary>
        /// Whether DCA buying is enabled.
        /// </summary>
        bool BuyDCAEnabled { get; set; }

        /// <summary>
        /// Multiplier applied to the DCA buy cost calculation.
        /// </summary>
        decimal BuyDCAMultiplier { get; }

        /// <summary>
        /// Minimum account balance required to place a DCA buy order.
        /// </summary>
        decimal BuyDCAMinBalance { get; }

        /// <summary>
        /// Timeout in minutes before the same pair can receive another DCA buy.
        /// </summary>
        double BuyDCASamePairTimeout { get; }

        /// <summary>
        /// Trailing percentage for DCA buy orders.
        /// </summary>
        decimal BuyDCATrailing { get; }

        /// <summary>
        /// Stop margin for trailing DCA buy.
        /// </summary>
        decimal BuyDCATrailingStopMargin { get; }

        /// <summary>
        /// Action to take when the trailing DCA buy stop margin is reached.
        /// </summary>
        BuyTrailingStopAction BuyDCATrailingStopAction { get; }
    }
}
