using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents an active trading position for a specific pair, including cost basis and current valuation.
    /// </summary>
    public interface ITradingPair
    {
        /// <summary>
        /// The trading pair symbol (e.g., "BTCUSDT").
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// The formatted display name for this pair.
        /// </summary>
        string FormattedName { get; }

        /// <summary>
        /// The current DCA (Dollar Cost Averaging) level (0 = initial buy, 1+ = DCA buys).
        /// </summary>
        int DCALevel { get; }

        /// <summary>
        /// Exchange-assigned order IDs for all buy orders in this position.
        /// </summary>
        List<string> OrderIds { get; }

        /// <summary>
        /// Timestamps of all buy orders in this position.
        /// </summary>
        List<DateTimeOffset> OrderDates { get; }

        /// <summary>
        /// Total amount held in the pair's base currency.
        /// </summary>
        decimal TotalAmount { get; }

        /// <summary>
        /// Volume-weighted average price paid across all buys.
        /// </summary>
        decimal AveragePricePaid { get; }

        /// <summary>
        /// Total fees paid in the pair's base currency.
        /// </summary>
        decimal FeesPairCurrency { get; }

        /// <summary>
        /// Total fees paid in the market currency.
        /// </summary>
        decimal FeesMarketCurrency { get; }

        /// <summary>
        /// Average cost per unit including fees.
        /// </summary>
        decimal AverageCostPaid { get; }

        /// <summary>
        /// Current cost of the position at market price (amount * current price).
        /// </summary>
        decimal CurrentCost { get; }

        /// <summary>
        /// The current market price.
        /// </summary>
        decimal CurrentPrice { get; }

        /// <summary>
        /// Current profit/loss margin as a percentage.
        /// </summary>
        decimal CurrentMargin { get; }

        /// <summary>
        /// Time in minutes since the first buy order.
        /// </summary>
        double CurrentAge { get; }

        /// <summary>
        /// Time in minutes since the most recent buy order.
        /// </summary>
        double LastBuyAge { get; }

        /// <summary>
        /// Additional metadata attached to this position.
        /// </summary>
        OrderMetadata Metadata { get; }

        /// <summary>
        /// Updates the current market price and recalculates derived values.
        /// </summary>
        /// <param name="currentPrice">The new current market price.</param>
        void SetCurrentPrice(decimal currentPrice);
    }
}
