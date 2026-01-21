using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Orchestrator responsible for sell operations including validation,
    /// trailing sell initiation, and order placement.
    /// </summary>
    public interface ISellOrchestrator
    {
        // Synchronous methods (for backward compatibility)
        /// <summary>
        /// Executes a sell operation with trailing sell support.
        /// </summary>
        /// <param name="options">Sell options including pair and amount.</param>
        void Sell(SellOptions options);

        /// <summary>
        /// Validates whether a sell operation can be executed.
        /// </summary>
        /// <param name="options">Sell options to validate.</param>
        /// <param name="message">Output message explaining why the sell cannot proceed, or null if valid.</param>
        /// <returns>True if sell can proceed; false otherwise.</returns>
        bool CanSell(SellOptions options, out string message);

        /// <summary>
        /// Places a sell order after validation.
        /// </summary>
        /// <param name="options">Sell options for the order.</param>
        /// <returns>Order details if successful; null otherwise.</returns>
        IOrderDetails PlaceSellOrder(SellOptions options);

        // Async methods (preferred for new code)
        /// <summary>
        /// Asynchronously executes a sell operation with trailing sell support.
        /// </summary>
        /// <param name="options">Sell options including pair and amount.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task SellAsync(SellOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously places a sell order after validation.
        /// </summary>
        /// <param name="options">Sell options for the order.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Order details if successful; null otherwise.</returns>
        Task<IOrderDetails> PlaceSellOrderAsync(SellOptions options, CancellationToken cancellationToken = default);
    }
}
