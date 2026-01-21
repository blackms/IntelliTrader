using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    public interface ITradingAccount : IDisposable
    {
        object SyncRoot { get; }

        // Synchronous methods (for backward compatibility)
        void Refresh();
        void Save();
        void AddOrder(IOrderDetails order);
        void AddBuyOrder(IOrderDetails order);
        ITradeResult AddSellOrder(IOrderDetails order);
        IOrderDetails PlaceOrder(IOrder order, OrderMetadata metadata);
        decimal GetBalance();
        bool HasTradingPair(string pair);
        ITradingPair GetTradingPair(string pair);
        IEnumerable<ITradingPair> GetTradingPairs();

        // Async methods (preferred for new code)
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
