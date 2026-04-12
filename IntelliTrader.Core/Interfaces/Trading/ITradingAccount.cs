using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Manages the trading account state including positions, orders, and balances.
    /// Provides both synchronous and asynchronous methods for order management.
    /// </summary>
    public interface ITradingAccount : IDisposable
    {
        /// <summary>
        /// Synchronization object for thread-safe account operations.
        /// </summary>
        object SyncRoot { get; }

        /// <summary>
        /// Refreshes account state from the exchange (balances, positions).
        /// </summary>
        void Refresh();

        /// <summary>
        /// Persists the current account state to disk.
        /// </summary>
        void Save();

        /// <summary>
        /// Records an order in the account (without updating position state).
        /// </summary>
        /// <param name="order">The order details to record.</param>
        void AddOrder(IOrderDetails order);

        /// <summary>
        /// Adds a buy order to the account and updates the corresponding trading pair position.
        /// </summary>
        /// <param name="order">The buy order details.</param>
        void AddBuyOrder(IOrderDetails order);

        /// <summary>
        /// Adds a sell order to the account, closes the position, and returns the trade result.
        /// </summary>
        /// <param name="order">The sell order details.</param>
        /// <returns>The calculated trade result including profit/loss.</returns>
        ITradeResult AddSellOrder(IOrderDetails order);

        /// <summary>
        /// Places an order on the exchange.
        /// </summary>
        /// <param name="order">The order to place.</param>
        /// <param name="metadata">Metadata to attach to the order.</param>
        /// <returns>The order details if successful; null otherwise.</returns>
        IOrderDetails PlaceOrder(IOrder order, OrderMetadata metadata);

        /// <summary>
        /// Gets the current available account balance in market currency.
        /// </summary>
        /// <returns>The available balance.</returns>
        decimal GetBalance();

        /// <summary>
        /// Checks if a trading pair has an active position.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>True if the pair has an active position.</returns>
        bool HasTradingPair(string pair);

        /// <summary>
        /// Gets the trading pair position data.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The trading pair position.</returns>
        ITradingPair GetTradingPair(string pair);

        /// <summary>
        /// Gets all active trading pair positions.
        /// </summary>
        /// <returns>Collection of active trading pairs.</returns>
        IEnumerable<ITradingPair> GetTradingPairs();

        /// <summary>
        /// Asynchronously refreshes the account state from the exchange.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task RefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously places an order on the exchange.
        /// </summary>
        /// <param name="order">The order to place.</param>
        /// <param name="metadata">Optional metadata to attach to the order.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Order details if successful; null otherwise.</returns>
        Task<IOrderDetails> PlaceOrderAsync(IOrder order, OrderMetadata metadata, CancellationToken cancellationToken = default);
    }
}
