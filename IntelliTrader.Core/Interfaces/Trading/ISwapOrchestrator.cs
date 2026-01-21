using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Orchestrator responsible for swap operations (selling one pair to buy another).
    /// </summary>
    public interface ISwapOrchestrator
    {
        // Synchronous methods (for backward compatibility)
        /// <summary>
        /// Executes a swap operation - sells old pair and buys new pair.
        /// </summary>
        /// <param name="options">Swap options including old pair, new pair, and metadata.</param>
        void Swap(SwapOptions options);

        /// <summary>
        /// Validates whether a swap operation can be executed.
        /// </summary>
        /// <param name="options">Swap options to validate.</param>
        /// <param name="message">Output message explaining why the swap cannot proceed, or null if valid.</param>
        /// <returns>True if swap can proceed; false otherwise.</returns>
        bool CanSwap(SwapOptions options, out string message);

        // Async methods (preferred for new code)
        /// <summary>
        /// Asynchronously executes a swap operation - sells old pair and buys new pair.
        /// </summary>
        /// <param name="options">Swap options including old pair, new pair, and metadata.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task SwapAsync(SwapOptions options, CancellationToken cancellationToken = default);
    }
}
