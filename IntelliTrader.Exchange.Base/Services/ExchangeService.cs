using ExchangeSharp;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Base
{
    public abstract class ExchangeService : ConfigrableServiceBase<ExchangeConfig>, IExchangeService
    {
        public override string ServiceName => Constants.ServiceNames.ExchangeService;

        protected readonly ILoggingService loggingService;
        protected readonly IHealthCheckService healthCheckService;
        protected readonly ICoreService coreService;

        public ExchangeService(ILoggingService loggingService, IHealthCheckService healthCheckService, ICoreService coreService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        }

        public abstract void Start(bool virtualTrading);

        public abstract void Stop();

        public abstract Task<IEnumerable<ITicker>> GetTickers(string market);

        public abstract Task<IEnumerable<string>> GetMarketPairs(string market);

        public abstract Task<Dictionary<string, decimal>> GetAvailableAmounts();

        public abstract Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair);

        public abstract Task<decimal> GetLastPrice(string pair);

        public abstract Task<IOrderDetails> PlaceOrder(IOrder order);
    }
}
