using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents the complete result of a closed trade including profit/loss calculations.
    /// </summary>
    public interface ITradeResult
    {
        /// <summary>
        /// Whether the trade executed successfully.
        /// </summary>
        bool IsSuccessful { get; }

        /// <summary>
        /// Whether the trade was part of a swap operation.
        /// </summary>
        bool IsSwap { get; }

        /// <summary>
        /// The trading pair symbol.
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// The total amount sold.
        /// </summary>
        decimal Amount { get; }

        /// <summary>
        /// The dates of all buy orders that built up this position.
        /// </summary>
        List<DateTimeOffset> OrderDates { get; }

        /// <summary>
        /// The volume-weighted average price paid across all buy orders.
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
        /// The average cost per unit including fees.
        /// </summary>
        decimal AverageCost { get; }

        /// <summary>
        /// The date and time of the sell order.
        /// </summary>
        DateTimeOffset SellDate { get; }

        /// <summary>
        /// The price at which the position was sold.
        /// </summary>
        decimal SellPrice { get; }

        /// <summary>
        /// The total cost received from the sell (amount * price).
        /// </summary>
        decimal SellCost { get; }

        /// <summary>
        /// The difference between sell proceeds and total cost paid.
        /// </summary>
        decimal BalanceDifference { get; }

        /// <summary>
        /// The profit percentage of the trade.
        /// </summary>
        decimal Profit { get; }

        /// <summary>
        /// Additional metadata about the trade.
        /// </summary>
        OrderMetadata Metadata { get; }

        /// <summary>
        /// Marks this trade result as part of a swap operation.
        /// </summary>
        /// <param name="isSwap">Whether this trade is a swap.</param>
        void SetSwap(bool isSwap);
    }
}
