using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents the detailed result of an executed order, including fill information and fees.
    /// </summary>
    public interface IOrderDetails
    {
        /// <summary>
        /// The order side (Buy or Sell).
        /// </summary>
        OrderSide Side { get; }

        /// <summary>
        /// The result status of the order (e.g., Filled, Error).
        /// </summary>
        OrderResult Result { get; }

        /// <summary>
        /// The date and time the order was executed.
        /// </summary>
        DateTimeOffset Date { get; }

        /// <summary>
        /// The exchange-assigned order identifier.
        /// </summary>
        string OrderId { get; }

        /// <summary>
        /// The trading pair symbol.
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// A human-readable message about the order result.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// The requested order amount.
        /// </summary>
        decimal Amount { get; }

        /// <summary>
        /// The actually filled amount (may differ from requested for partial fills).
        /// </summary>
        decimal AmountFilled { get; }

        /// <summary>
        /// The requested order price.
        /// </summary>
        decimal Price { get; }

        /// <summary>
        /// The volume-weighted average fill price.
        /// </summary>
        decimal AveragePrice { get; }

        /// <summary>
        /// The total fees paid for this order.
        /// </summary>
        decimal Fees { get; }

        /// <summary>
        /// The currency in which fees were charged.
        /// </summary>
        string FeesCurrency { get; }

        /// <summary>
        /// The average cost per unit including fees.
        /// </summary>
        decimal AverageCost { get; }

        /// <summary>
        /// Additional metadata attached to the order (e.g., signal rule, swap info).
        /// </summary>
        OrderMetadata Metadata { get; }

        /// <summary>
        /// Attaches metadata to this order.
        /// </summary>
        /// <param name="metadata">The metadata to attach.</param>
        void SetMetadata(OrderMetadata metadata);
    }
}
