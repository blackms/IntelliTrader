using ExchangeSharp;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Base
{
    public abstract class ExchangeService(
        ILoggingService loggingService,
        IHealthCheckService healthCheckService,
        ICoreService coreService,
        IConfigProvider configProvider) : ConfigurableServiceBase<ExchangeConfig>(configProvider), IExchangeService
    {
        public override string ServiceName => Constants.ServiceNames.ExchangeService;

        protected override ILoggingService LoggingService => loggingService;
        protected IHealthCheckService HealthCheckService => healthCheckService;
        protected ICoreService CoreService => coreService;

        /// <inheritdoc />
        public virtual event Action<IReadOnlyCollection<ITicker>>? TickersUpdated;

        /// <inheritdoc />
        public virtual bool IsWebSocketConnected => false;

        /// <inheritdoc />
        public virtual bool IsRestFallbackActive => false;

        /// <inheritdoc />
        public virtual TimeSpan TimeSinceLastTickerUpdate => TimeSpan.Zero;

        public abstract void Start(bool virtualTrading);

        public abstract void Stop();

        public abstract Task<IEnumerable<ITicker>> GetTickers(string market);

        public abstract Task<IEnumerable<string>> GetMarketPairs(string market);

        public abstract Task<Dictionary<string, decimal>> GetAvailableAmounts();

        public abstract Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair);

        public abstract Task<decimal> GetLastPrice(string pair);

        public abstract Task<IOrderDetails> PlaceOrder(IOrder order);

        /// <inheritdoc />
        public virtual Task ReconnectWebSocketAsync()
        {
            LoggingService.Debug("WebSocket reconnect not implemented for this exchange");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Raises the TickersUpdated event.
        /// </summary>
        protected virtual void OnTickersUpdated(IReadOnlyCollection<ITicker> tickers)
        {
            TickersUpdated?.Invoke(tickers);
        }
    }
}
