using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    public interface ITradingService : IConfigurableService
    {
        ITradingConfig Config { get; }
        IModuleRules Rules { get; }
        bool IsTradingSuspended { get; }
        void Start();
        void Stop();
        void ResumeTrading(bool forced = false);
        void SuspendTrading(bool forced = false);
        ITradingAccount Account { get; }

        /// <summary>
        /// Gets the bounded order history collection. Oldest orders are automatically removed when MaxOrderHistorySize is exceeded.
        /// </summary>
        BoundedConcurrentStack<IOrderDetails> OrderHistory { get; }
        IPairConfig GetPairConfig(string pair);
        void ReapplyTradingRules();

        // Synchronous trading methods (for backward compatibility)
        void Buy(BuyOptions options);
        void Sell(SellOptions options);
        void Swap(SwapOptions options);

        // Async trading methods (preferred for new code)
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

        bool CanBuy(BuyOptions options, out string message);
        bool CanSell(SellOptions options, out string message);
        bool CanSwap(SwapOptions options, out string message);
        void LogOrder(IOrderDetails order);
        List<string> GetTrailingBuys();
        List<string> GetTrailingSells();

        // Synchronous methods (for backward compatibility and use in locked sections)
        IEnumerable<ITicker> GetTickers();
        IEnumerable<string> GetMarketPairs();
        Dictionary<string, decimal> GetAvailableAmounts();
        IEnumerable<IOrderDetails> GetMyTrades(string pair);
        IOrderDetails PlaceOrder(IOrder order);
        decimal GetCurrentPrice(string pair);

        // Async methods (preferred for new code)
        Task<IEnumerable<ITicker>> GetTickersAsync();
        Task<IEnumerable<string>> GetMarketPairsAsync();
        Task<Dictionary<string, decimal>> GetAvailableAmountsAsync();
        Task<IEnumerable<IOrderDetails>> GetMyTradesAsync(string pair);
        Task<IOrderDetails> PlaceOrderAsync(IOrder order);
        Task<decimal> GetCurrentPriceAsync(string pair);
    }
}
