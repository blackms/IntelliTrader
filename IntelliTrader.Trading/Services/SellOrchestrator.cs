using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Orchestrator responsible for sell operations including validation,
    /// trailing sell initiation, and order placement.
    /// </summary>
    internal class SellOrchestrator : ISellOrchestrator
    {
        private const int MIN_INTERVAL_BETWEEN_BUY_AND_SELL = 10000;

        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly ITrailingOrderManager _trailingManager;
        private readonly Func<ITradingConfig> _configProvider;
        private readonly Func<string, IPairConfig> _pairConfigProvider;
        private readonly Func<ITradingAccount> _accountProvider;
        private readonly Func<bool> _tradingSuspendedProvider;
        private readonly Func<string, decimal> _priceProvider;
        private readonly Func<BoundedConcurrentStack<IOrderDetails>> _orderHistoryProvider;
        private readonly Action _reapplyTradingRules;
        private readonly IApplicationContext _applicationContext;

        public SellOrchestrator(
            ILoggingService loggingService,
            INotificationService notificationService,
            ITrailingOrderManager trailingManager,
            Func<ITradingConfig> configProvider,
            Func<string, IPairConfig> pairConfigProvider,
            Func<ITradingAccount> accountProvider,
            Func<bool> tradingSuspendedProvider,
            Func<string, decimal> priceProvider,
            Func<BoundedConcurrentStack<IOrderDetails>> orderHistoryProvider,
            Action reapplyTradingRules,
            IApplicationContext applicationContext)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _trailingManager = trailingManager ?? throw new ArgumentNullException(nameof(trailingManager));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _pairConfigProvider = pairConfigProvider ?? throw new ArgumentNullException(nameof(pairConfigProvider));
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _tradingSuspendedProvider = tradingSuspendedProvider ?? throw new ArgumentNullException(nameof(tradingSuspendedProvider));
            _priceProvider = priceProvider ?? throw new ArgumentNullException(nameof(priceProvider));
            _orderHistoryProvider = orderHistoryProvider ?? throw new ArgumentNullException(nameof(orderHistoryProvider));
            _reapplyTradingRules = reapplyTradingRules ?? throw new ArgumentNullException(nameof(reapplyTradingRules));
            _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        }

        private ITradingConfig Config => _configProvider();
        private ITradingAccount Account => _accountProvider();
        private bool IsTradingSuspended => _tradingSuspendedProvider();
        private BoundedConcurrentStack<IOrderDetails> OrderHistory => _orderHistoryProvider();

        private IPairConfig GetPairConfig(string pair) => _pairConfigProvider(pair);
        private decimal GetCurrentPrice(string pair) => _priceProvider(pair);

        public void Sell(SellOptions options)
        {
            if (CanSell(options, out string message))
            {
                InitiateSell(options);
            }
            else
            {
                _loggingService.Debug(message);
            }
        }

        private void InitiateSell(SellOptions options)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.SellTrailing != 0)
            {
                // Initiate trailing sell
                _trailingManager.InitiateTrailingSell(options.Pair);
            }
            else
            {
                PlaceSellOrder(options);
            }
        }

        public bool CanSell(SellOptions options, out string message)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && IsTradingSuspended)
            {
                message = $"Cancel sell request for {options.Pair}. Reason: trading suspended";
                return false;
            }
            else if (!options.ManualOrder && !pairConfig.SellEnabled)
            {
                message = $"Cancel sell request for {options.Pair}. Reason: selling not enabled";
                return false;
            }
            else if (!options.ManualOrder && Config.ExcludedPairs.Contains(options.Pair))
            {
                message = $"Cancel sell request for {options.Pair}. Reason: excluded pair";
                return false;
            }
            else if (!Account.HasTradingPair(options.Pair))
            {
                message = $"Cancel sell request for {options.Pair}. Reason: pair does not exist";
                return false;
            }
            else if ((DateTimeOffset.Now - Account.GetTradingPair(options.Pair).OrderDates.Max()).TotalMilliseconds < (MIN_INTERVAL_BETWEEN_BUY_AND_SELL / _applicationContext.Speed))
            {
                message = $"Cancel sell request for {options.Pair}. Reason: pair just bought";
                return false;
            }
            message = null;
            return true;
        }

        public IOrderDetails PlaceSellOrder(SellOptions options)
        {
            IOrderDetails orderDetails = null;
            _trailingManager.CancelTrailingSell(options.Pair);
            _trailingManager.CancelTrailingBuy(options.Pair);

            if (CanSell(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                tradingPair.SetCurrentPrice(GetCurrentPrice(options.Pair));

                var orderRequest = new SellOrder
                {
                    Type = pairConfig.SellType,
                    Pair = options.Pair,
                    Amount = tradingPair.TotalAmount,
                    Price = tradingPair.CurrentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, null);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    _loggingService.Info($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    _ = _notificationService.NotifyAsync($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    OrderHistory.Push(orderDetails);
                }
                else
                {
                    _loggingService.Info($"Unable to place sell order for {options.Pair}. Order result: {orderDetails?.Result}");
                }

                _reapplyTradingRules();
            }
            else
            {
                _loggingService.Info(message);
            }
            return orderDetails;
        }

        // Async methods (preferred for new code)
        public Task SellAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            if (CanSell(options, out string message))
            {
                return InitiateSellAsync(options, cancellationToken);
            }
            else
            {
                _loggingService.Debug(message);
                return Task.CompletedTask;
            }
        }

        private async Task InitiateSellAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.SellTrailing != 0)
            {
                // Initiate trailing sell (synchronous operation)
                _trailingManager.InitiateTrailingSell(options.Pair);
            }
            else
            {
                await PlaceSellOrderAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IOrderDetails> PlaceSellOrderAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            IOrderDetails orderDetails = null;
            _trailingManager.CancelTrailingSell(options.Pair);
            _trailingManager.CancelTrailingBuy(options.Pair);

            if (CanSell(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                tradingPair.SetCurrentPrice(GetCurrentPrice(options.Pair));

                var orderRequest = new SellOrder
                {
                    Type = pairConfig.SellType,
                    Pair = options.Pair,
                    Amount = tradingPair.TotalAmount,
                    Price = tradingPair.CurrentPrice
                };

                // Use async order placement for non-blocking I/O
                orderDetails = await Account.PlaceOrderAsync(orderRequest, null, cancellationToken).ConfigureAwait(false);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    _loggingService.Info($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    _ = _notificationService.NotifyAsync($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    OrderHistory.Push(orderDetails);
                }
                else
                {
                    _loggingService.Info($"Unable to place sell order for {options.Pair}. Order result: {orderDetails?.Result}");
                }

                _reapplyTradingRules();
            }
            else
            {
                _loggingService.Info(message);
            }
            return orderDetails;
        }
    }
}
