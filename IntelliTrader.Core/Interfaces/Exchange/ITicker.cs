using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents real-time price data for a trading pair from the exchange.
    /// </summary>
    public interface ITicker
    {
        /// <summary>
        /// The trading pair symbol (e.g., "BTCUSDT").
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// The current best bid (buy) price.
        /// </summary>
        decimal BidPrice { get; }

        /// <summary>
        /// The current best ask (sell) price.
        /// </summary>
        decimal AskPrice { get; }

        /// <summary>
        /// The price of the last executed trade.
        /// </summary>
        decimal LastPrice { get; }
    }
}
