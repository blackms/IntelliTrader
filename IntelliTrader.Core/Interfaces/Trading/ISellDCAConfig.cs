using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for sell parameters specific to DCA (Dollar Cost Averaging) positions.
    /// </summary>
    public interface ISellDCAConfig
    {
        /// <summary>
        /// Target profit margin percentage for DCA positions.
        /// </summary>
        decimal SellDCAMargin { get; }

        /// <summary>
        /// Trailing percentage for DCA sell orders.
        /// </summary>
        decimal SellDCATrailing { get; }

        /// <summary>
        /// Stop margin for trailing DCA sell.
        /// </summary>
        decimal SellDCATrailingStopMargin { get; }

        /// <summary>
        /// Action to take when the trailing DCA sell stop margin is reached.
        /// </summary>
        SellTrailingStopAction SellDCATrailingStopAction { get; }
    }
}
