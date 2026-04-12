using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service abstracting the exchange API for market data retrieval and order execution.
    /// Supports both WebSocket streaming and REST polling with automatic fallback.
    /// </summary>
    public interface IExchangeService : IConfigurableService
    {
        /// <summary>
        /// Event raised when ticker data is updated (from WebSocket or REST).
        /// </summary>
        event Action<IReadOnlyCollection<ITicker>> TickersUpdated;

        /// <summary>
        /// Gets whether the exchange service is using WebSocket streaming.
        /// </summary>
        bool IsWebSocketConnected { get; }

        /// <summary>
        /// Gets whether the exchange service has fallen back to REST polling.
        /// </summary>
        bool IsRestFallbackActive { get; }

        /// <summary>
        /// Gets the time elapsed since the last ticker update.
        /// </summary>
        TimeSpan TimeSinceLastTickerUpdate { get; }

        /// <summary>
        /// Starts the exchange service and connects to the exchange API.
        /// </summary>
        /// <param name="virtualTrading">Whether to run in virtual (paper) trading mode.</param>
        void Start(bool virtualTrading);

        /// <summary>
        /// Stops the exchange service and disconnects from the API.
        /// </summary>
        void Stop();

        /// <summary>
        /// Gets current ticker data for all pairs in a market.
        /// </summary>
        /// <param name="market">The market name (e.g., "BTC", "USDT").</param>
        /// <returns>Collection of tickers for the market.</returns>
        Task<IEnumerable<ITicker>> GetTickers(string market);

        /// <summary>
        /// Gets all available trading pair symbols for a market.
        /// </summary>
        /// <param name="market">The market name.</param>
        /// <returns>Collection of pair symbols.</returns>
        Task<IEnumerable<string>> GetMarketPairs(string market);

        /// <summary>
        /// Gets available asset amounts (balances) from the exchange account.
        /// </summary>
        /// <returns>Dictionary of asset symbol to available amount.</returns>
        Task<Dictionary<string, decimal>> GetAvailableAmounts();

        /// <summary>
        /// Gets the user's recent trade history for a specific pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>Collection of order details for the pair.</returns>
        Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair);

        /// <summary>
        /// Gets the last traded price for a pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The last trade price.</returns>
        Task<decimal> GetLastPrice(string pair);

        /// <summary>
        /// Places an order on the exchange.
        /// </summary>
        /// <param name="order">The order to place.</param>
        /// <returns>The order execution details.</returns>
        Task<IOrderDetails> PlaceOrder(IOrder order);

        /// <summary>
        /// Forces a reconnection of the WebSocket stream.
        /// </summary>
        Task ReconnectWebSocketAsync();

        /// <summary>
        /// Updates the exchange API credentials at runtime without restarting the service.
        /// Returns true if credentials were successfully applied.
        /// </summary>
        bool UpdateCredentials(string keysFilePath);
    }
}
