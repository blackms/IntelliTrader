using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Central trading service that coordinates buy/sell/swap operations, manages positions, and provides market data access.
    /// </summary>
    public interface ITradingService : IConfigurableService
    {
        /// <summary>
        /// The trading configuration.
        /// </summary>
        ITradingConfig Config { get; }

        /// <summary>
        /// The trading rules module containing sell/DCA rules.
        /// </summary>
        IModuleRules Rules { get; }

        /// <summary>
        /// Whether trading is currently suspended (e.g., due to health check failures or circuit breaker).
        /// </summary>
        bool IsTradingSuspended { get; }

        /// <summary>
        /// Starts the trading service and initializes the trading account.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the trading service.
        /// </summary>
        void Stop();

        /// <summary>
        /// Resumes trading after a suspension.
        /// </summary>
        /// <param name="forced">If true, resumes even if automatic conditions are not met.</param>
        void ResumeTrading(bool forced = false);

        /// <summary>
        /// Suspends all trading operations.
        /// </summary>
        /// <param name="forced">If true, prevents automatic resumption.</param>
        void SuspendTrading(bool forced = false);

        /// <summary>
        /// The trading account managing positions and balances.
        /// </summary>
        ITradingAccount Account { get; }

        /// <summary>
        /// Gets the bounded order history collection. Oldest orders are automatically removed when MaxOrderHistorySize is exceeded.
        /// </summary>
        BoundedConcurrentStack<IOrderDetails> OrderHistory { get; }

        /// <summary>
        /// Gets the pair-specific configuration, which may override global settings.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The effective configuration for the pair.</returns>
        IPairConfig GetPairConfig(string pair);

        /// <summary>
        /// Reapplies trading rules from the current configuration (used after hot-reload).
        /// </summary>
        void ReapplyTradingRules();

        /// <summary>
        /// Executes a buy operation with swap detection and trailing buy support.
        /// </summary>
        /// <param name="options">Buy options including pair, amount, and metadata.</param>
        void Buy(BuyOptions options);

        /// <summary>
        /// Executes a sell operation with trailing sell support.
        /// </summary>
        /// <param name="options">Sell options including pair and amount.</param>
        void Sell(SellOptions options);

        /// <summary>
        /// Executes a swap operation (sell one pair and buy another).
        /// </summary>
        /// <param name="options">Swap options including old pair, new pair, and metadata.</param>
        void Swap(SwapOptions options);

        /// <summary>
        /// Asynchronously executes a buy operation with swap detection and trailing buy support.
        /// </summary>
        /// <param name="options">Buy options including pair, amount, and metadata.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task BuyAsync(BuyOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes a sell operation with trailing sell support.
        /// </summary>
        /// <param name="options">Sell options including pair and amount.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task SellAsync(SellOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes a swap operation - sells old pair and buys new pair.
        /// </summary>
        /// <param name="options">Swap options including old pair, new pair, and metadata.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task SwapAsync(SwapOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates whether a buy operation can be executed.
        /// </summary>
        /// <param name="options">Buy options to validate.</param>
        /// <param name="message">Output message explaining why the buy cannot proceed, or null if valid.</param>
        /// <returns>True if buy can proceed; false otherwise.</returns>
        bool CanBuy(BuyOptions options, out string message);

        /// <summary>
        /// Validates whether a sell operation can be executed.
        /// </summary>
        /// <param name="options">Sell options to validate.</param>
        /// <param name="message">Output message explaining why the sell cannot proceed, or null if valid.</param>
        /// <returns>True if sell can proceed; false otherwise.</returns>
        bool CanSell(SellOptions options, out string message);

        /// <summary>
        /// Validates whether a swap operation can be executed.
        /// </summary>
        /// <param name="options">Swap options to validate.</param>
        /// <param name="message">Output message explaining why the swap cannot proceed, or null if valid.</param>
        /// <returns>True if swap can proceed; false otherwise.</returns>
        bool CanSwap(SwapOptions options, out string message);

        /// <summary>
        /// Logs an executed order to the order history.
        /// </summary>
        /// <param name="order">The order details to log.</param>
        void LogOrder(IOrderDetails order);

        /// <summary>
        /// Gets the list of pairs with active trailing buy orders.
        /// </summary>
        /// <returns>List of pair symbols.</returns>
        List<string> GetTrailingBuys();

        /// <summary>
        /// Gets the list of pairs with active trailing sell orders.
        /// </summary>
        /// <returns>List of pair symbols.</returns>
        List<string> GetTrailingSells();

        /// <summary>
        /// Gets current ticker data for the configured market.
        /// </summary>
        /// <returns>Collection of tickers.</returns>
        IEnumerable<ITicker> GetTickers();

        /// <summary>
        /// Gets all trading pair symbols for the configured market.
        /// </summary>
        /// <returns>Collection of pair symbols.</returns>
        IEnumerable<string> GetMarketPairs();

        /// <summary>
        /// Gets available asset amounts from the exchange.
        /// </summary>
        /// <returns>Dictionary of asset to available amount.</returns>
        Dictionary<string, decimal> GetAvailableAmounts();

        /// <summary>
        /// Gets the user's trade history for a pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>Collection of order details.</returns>
        IEnumerable<IOrderDetails> GetMyTrades(string pair);

        /// <summary>
        /// Places an order on the exchange through the trading account.
        /// </summary>
        /// <param name="order">The order to place.</param>
        /// <returns>The order execution details.</returns>
        IOrderDetails PlaceOrder(IOrder order);

        /// <summary>
        /// Gets the current market price for a pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The current price.</returns>
        decimal GetCurrentPrice(string pair);

        /// <summary>
        /// Asynchronously gets current ticker data for the configured market.
        /// </summary>
        /// <returns>Collection of tickers.</returns>
        Task<IEnumerable<ITicker>> GetTickersAsync();

        /// <summary>
        /// Asynchronously gets all trading pair symbols for the configured market.
        /// </summary>
        /// <returns>Collection of pair symbols.</returns>
        Task<IEnumerable<string>> GetMarketPairsAsync();

        /// <summary>
        /// Asynchronously gets available asset amounts from the exchange.
        /// </summary>
        /// <returns>Dictionary of asset to available amount.</returns>
        Task<Dictionary<string, decimal>> GetAvailableAmountsAsync();

        /// <summary>
        /// Asynchronously gets the user's trade history for a pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>Collection of order details.</returns>
        Task<IEnumerable<IOrderDetails>> GetMyTradesAsync(string pair);

        /// <summary>
        /// Asynchronously places an order on the exchange.
        /// </summary>
        /// <param name="order">The order to place.</param>
        /// <returns>The order execution details.</returns>
        Task<IOrderDetails> PlaceOrderAsync(IOrder order);

        /// <summary>
        /// Asynchronously gets the current market price for a pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>The current price.</returns>
        Task<decimal> GetCurrentPriceAsync(string pair);
    }
}
