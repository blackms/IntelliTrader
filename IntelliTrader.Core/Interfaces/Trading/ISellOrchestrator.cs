using System;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Orchestrator responsible for sell operations including validation,
    /// trailing sell initiation, and order placement.
    /// </summary>
    public interface ISellOrchestrator
    {
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
    }
}
