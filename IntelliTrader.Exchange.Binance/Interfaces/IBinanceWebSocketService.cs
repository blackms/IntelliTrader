using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliTrader.Core;

namespace IntelliTrader.Exchange.Binance
{
    /// <summary>
    /// Service interface for Binance WebSocket streaming.
    /// Provides real-time ticker data via WebSocket with automatic reconnection and REST fallback.
    /// </summary>
    public interface IBinanceWebSocketService : IDisposable
    {
        /// <summary>
        /// Event raised when ticker data is received from the WebSocket stream.
        /// </summary>
        event Action<IReadOnlyCollection<ITicker>> TickersUpdated;

        /// <summary>
        /// Event raised when the WebSocket connection state changes.
        /// </summary>
        event Action<WebSocketConnectionState> ConnectionStateChanged;

        /// <summary>
        /// Gets the current connection state of the WebSocket.
        /// </summary>
        WebSocketConnectionState ConnectionState { get; }

        /// <summary>
        /// Gets the time elapsed since the last successful ticker update.
        /// </summary>
        TimeSpan TimeSinceLastUpdate { get; }

        /// <summary>
        /// Gets whether the WebSocket is currently connected and receiving data.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets whether REST fallback mode is currently active.
        /// </summary>
        bool IsRestFallbackActive { get; }

        /// <summary>
        /// Connects to the Binance WebSocket stream for all tickers.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the connection operation.</returns>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnects from the Binance WebSocket stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the disconnection operation.</returns>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to ticker updates for specific trading pairs.
        /// </summary>
        /// <param name="pairs">Collection of trading pairs to subscribe to (e.g., "BTCUSDT", "ETHUSDT").</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the subscription operation.</returns>
        Task SubscribeToTickersAsync(IEnumerable<string> pairs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unsubscribes from ticker updates for specific trading pairs.
        /// </summary>
        /// <param name="pairs">Collection of trading pairs to unsubscribe from.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the unsubscription operation.</returns>
        Task UnsubscribeFromTickersAsync(IEnumerable<string> pairs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces an immediate reconnection to the WebSocket stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the reconnection operation.</returns>
        Task ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches current tickers via REST API (used as fallback when WebSocket is unavailable).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Collection of current ticker data.</returns>
        Task<IReadOnlyCollection<ITicker>> FetchTickersViaRestAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents the connection state of the WebSocket.
    /// </summary>
    public enum WebSocketConnectionState
    {
        /// <summary>WebSocket is disconnected.</summary>
        Disconnected,

        /// <summary>WebSocket is attempting to connect.</summary>
        Connecting,

        /// <summary>WebSocket is connected and receiving data.</summary>
        Connected,

        /// <summary>WebSocket is reconnecting after a connection loss.</summary>
        Reconnecting,

        /// <summary>WebSocket connection failed, using REST fallback.</summary>
        FallbackToRest,

        /// <summary>WebSocket is disconnecting.</summary>
        Disconnecting
    }
}
