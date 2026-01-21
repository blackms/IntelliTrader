using System;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Orchestrator responsible for buy operations including validation,
    /// trailing buy initiation, and order placement.
    /// </summary>
    public interface IBuyOrchestrator
    {
        /// <summary>
        /// Executes a buy operation with swap detection and trailing buy support.
        /// </summary>
        /// <param name="options">Buy options including pair, amount, and metadata.</param>
        void Buy(BuyOptions options);

        /// <summary>
        /// Validates whether a buy operation can be executed.
        /// </summary>
        /// <param name="options">Buy options to validate.</param>
        /// <param name="message">Output message explaining why the buy cannot proceed, or null if valid.</param>
        /// <returns>True if buy can proceed; false otherwise.</returns>
        bool CanBuy(BuyOptions options, out string message);

        /// <summary>
        /// Places a buy order after validation.
        /// </summary>
        /// <param name="options">Buy options for the order.</param>
        /// <returns>Order details if successful; null otherwise.</returns>
        IOrderDetails PlaceBuyOrder(BuyOptions options);
    }
}
