using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents an order to be placed on the exchange.
    /// </summary>
    public interface IOrder
    {
        /// <summary>
        /// The order side (Buy or Sell).
        /// </summary>
        OrderSide Side { get; }

        /// <summary>
        /// The order type (Market or Limit).
        /// </summary>
        OrderType Type { get; }

        /// <summary>
        /// The date and time the order was created.
        /// </summary>
        DateTimeOffset Date { get; }

        /// <summary>
        /// The trading pair symbol (e.g., "BTCUSDT").
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// The order amount in the pair's base currency.
        /// </summary>
        decimal Amount { get; }

        /// <summary>
        /// The order price (for limit orders).
        /// </summary>
        decimal Price { get; }
    }
}
