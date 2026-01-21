using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
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

        void Start(bool virtualTrading);
        void Stop();
        Task<IEnumerable<ITicker>> GetTickers(string market);
        Task<IEnumerable<string>> GetMarketPairs(string market);
        Task<Dictionary<string, decimal>> GetAvailableAmounts();
        Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair);
        Task<decimal> GetLastPrice(string pair);
        Task<IOrderDetails> PlaceOrder(IOrder order);

        /// <summary>
        /// Forces a reconnection of the WebSocket stream.
        /// </summary>
        Task ReconnectWebSocketAsync();
    }
}
