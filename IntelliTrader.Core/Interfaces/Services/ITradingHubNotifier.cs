using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Interface for broadcasting real-time trading updates to connected clients via SignalR.
    /// </summary>
    public interface ITradingHubNotifier
    {
        /// <summary>
        /// Broadcasts a price update for a specific trading pair to all subscribed clients.
        /// </summary>
        /// <param name="pair">The trading pair (e.g., "BTCUSDT").</param>
        /// <param name="price">The current price.</param>
        Task BroadcastPriceUpdateAsync(string pair, decimal price);

        /// <summary>
        /// Broadcasts a price update for a specific trading pair to all subscribed clients.
        /// </summary>
        /// <param name="ticker">The ticker with price information.</param>
        Task BroadcastTickerUpdateAsync(ITicker ticker);

        /// <summary>
        /// Broadcasts trade execution details to all connected clients.
        /// </summary>
        /// <param name="orderDetails">The details of the executed trade.</param>
        Task BroadcastTradeExecutedAsync(IOrderDetails orderDetails);

        /// <summary>
        /// Broadcasts position change for a trading pair to all connected clients.
        /// </summary>
        /// <param name="tradingPair">The trading pair that changed.</param>
        /// <param name="changeType">The type of change (e.g., "Added", "Updated", "Removed").</param>
        Task BroadcastPositionChangedAsync(ITradingPair tradingPair, string changeType);

        /// <summary>
        /// Broadcasts the current health status to all connected clients.
        /// </summary>
        /// <param name="healthChecks">Collection of health check results.</param>
        /// <param name="tradingSuspended">Whether trading is currently suspended.</param>
        Task BroadcastHealthStatusAsync(System.Collections.Generic.IEnumerable<IHealthCheck> healthChecks, bool tradingSuspended);

        /// <summary>
        /// Broadcasts account balance update to all connected clients.
        /// </summary>
        /// <param name="balance">The current account balance.</param>
        Task BroadcastBalanceUpdateAsync(decimal balance);

        /// <summary>
        /// Broadcasts trailing status updates (buys, sells, signals) to all connected clients.
        /// </summary>
        /// <param name="trailingBuys">List of pairs with active trailing buys.</param>
        /// <param name="trailingSells">List of pairs with active trailing sells.</param>
        /// <param name="trailingSignals">List of signals with active trailing.</param>
        Task BroadcastTrailingStatusAsync(
            System.Collections.Generic.List<string> trailingBuys,
            System.Collections.Generic.List<string> trailingSells,
            System.Collections.Generic.List<string> trailingSignals);

        /// <summary>
        /// Broadcasts a complete status update to all connected clients.
        /// </summary>
        /// <param name="status">The complete status object.</param>
        Task BroadcastStatusUpdateAsync(object status);
    }
}
