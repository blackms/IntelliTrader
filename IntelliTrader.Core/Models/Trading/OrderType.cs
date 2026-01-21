using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Order type for exchange operations.
    /// Values are aligned with ExchangeSharp.OrderType enum.
    /// </summary>
    public enum OrderType
    {
        /// <summary>
        /// A limit order - will not buy or sell beyond the specified price.
        /// </summary>
        Limit,

        /// <summary>
        /// A market order - executes at current market price.
        /// Use with caution as this may result in unfavorable prices if order book is thin.
        /// </summary>
        Market,

        /// <summary>
        /// A stop order - triggers when price reaches a specified level.
        /// </summary>
        Stop
    }
}
