using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Orchestrator responsible for buy operations including validation,
    /// trailing buy initiation, and order placement.
    /// </summary>
    internal class BuyOrchestrator : IBuyOrchestrator
    {
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
        private readonly IPortfolioRiskManager _portfolioRiskManager;

        public BuyOrchestrator(
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
            IPortfolioRiskManager portfolioRiskManager = null)
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
            _portfolioRiskManager = portfolioRiskManager;
        }

        private ITradingConfig Config => _configProvider();
        private ITradingAccount Account => _accountProvider();
        private bool IsTradingSuspended => _tradingSuspendedProvider();
        private BoundedConcurrentStack<IOrderDetails> OrderHistory => _orderHistoryProvider();

        private IPairConfig GetPairConfig(string pair) => _pairConfigProvider(pair);
        private decimal GetCurrentPrice(string pair) => _priceProvider(pair);

        public void Buy(BuyOptions options)
        {
            if (CanBuy(options, out string message))
            {
                InitiateBuy(options);
            }
            else
            {
                _loggingService.Debug(message);
            }
        }

        private void InitiateBuy(BuyOptions options)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.BuyTrailing != 0)
            {
                // Initiate trailing buy
                _trailingManager.InitiateTrailingBuy(options.Pair);
            }
            else
            {
                PlaceBuyOrder(options);
            }
        }

        public bool CanBuy(BuyOptions options, out string message)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && IsTradingSuspended)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: trading suspended";
                return false;
            }
            else if (!options.ManualOrder && !options.Swap && !pairConfig.BuyEnabled)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: buying not enabled";
                return false;
            }
            else if (!options.ManualOrder && Config.ExcludedPairs.Contains(options.Pair))
            {
                message = $"Cancel buy request for {options.Pair}. Reason: exluded pair";
                return false;
            }
            else if (!options.ManualOrder && !options.IgnoreExisting && Account.HasTradingPair(options.Pair))
            {
                message = $"Cancel buy request for {options.Pair}. Reason: pair already exists";
                return false;
            }
            else if (!options.ManualOrder && !options.Swap && Config.MaxPairs != 0 && Account.GetTradingPairs().Count() >= Config.MaxPairs && !Account.HasTradingPair(options.Pair))
            {
                message = $"Cancel buy request for {options.Pair}. Reason: maximum pairs reached";
                return false;
            }
            else if (!options.ManualOrder && !options.Swap && pairConfig.BuyMinBalance != 0 && (Account.GetBalance() - options.MaxCost) < pairConfig.BuyMinBalance)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: minimum balance reached";
                return false;
            }
            else if (GetCurrentPrice(options.Pair) <= 0)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: invalid price";
                return false;
            }
            else if (Account.GetBalance() < options.MaxCost)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: not enough balance";
                return false;
            }
            else if (options.Amount == null && options.MaxCost == null || options.Amount != null && options.MaxCost != null)
            {
                message = $"Cancel buy request for {options.Pair}. Reason: either max cost or amount needs to be specified (not both)";
                return false;
            }
            else if (!options.ManualOrder && !options.Swap && pairConfig.BuySamePairTimeout > 0 && OrderHistory.Any(h => h.Side == OrderSide.Buy && h.Pair == options.Pair) &&
                (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds < pairConfig.BuySamePairTimeout)
            {
                var elapsedSeconds = (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds;
                message = $"Cancel buy request for {options.Pair}. Reason: buy same pair timeout (elapsed: {elapsedSeconds:0.#}, timeout: {pairConfig.BuySamePairTimeout:0.#})";
                return false;
            }
            // Portfolio risk management checks
            else if (!options.ManualOrder && !options.Swap && _portfolioRiskManager != null && Config.RiskManagement?.Enabled == true)
            {
                // Check circuit breaker
                if (_portfolioRiskManager.IsCircuitBreakerTriggered())
                {
                    message = $"Cancel buy request for {options.Pair}. Reason: circuit breaker triggered (drawdown limit exceeded)";
                    return false;
                }

                // Check daily loss limit
                if (_portfolioRiskManager.IsDailyLossLimitReached())
                {
                    message = $"Cancel buy request for {options.Pair}. Reason: daily loss limit reached ({_portfolioRiskManager.GetDailyProfitLoss():0.00}%)";
                    return false;
                }

                // Check portfolio heat
                decimal positionRisk = Config.RiskManagement.DefaultPositionRiskPercent;
                if (_portfolioRiskManager is PortfolioRiskManager prm && options.MaxCost.HasValue)
                {
                    positionRisk = prm.CalculatePositionRisk(options.MaxCost.Value, Math.Abs(Config.SellStopLossMargin));
                }

                if (!_portfolioRiskManager.CanOpenPosition(positionRisk))
                {
                    decimal currentHeat = _portfolioRiskManager.GetCurrentHeat();
                    message = $"Cancel buy request for {options.Pair}. Reason: portfolio heat limit reached (current: {currentHeat:0.00}%, max: {Config.RiskManagement.MaxPortfolioHeat:0.00}%)";
                    return false;
                }
            }

            message = null;
            return true;
        }

        public IOrderDetails PlaceBuyOrder(BuyOptions options)
        {
            IOrderDetails orderDetails = null;
            _trailingManager.CancelTrailingBuy(options.Pair);
            _trailingManager.CancelTrailingSell(options.Pair);

            if (CanBuy(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = GetCurrentPrice(options.Pair);
                decimal amount = options.Amount ?? Math.Round(options.MaxCost.Value / currentPrice, 4);

                var orderRequest = new BuyOrder
                {
                    Type = pairConfig.BuyType,
                    Pair = options.Pair,
                    Amount = amount,
                    Price = currentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, options.Metadata);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    string newPairName = tradingPair != null ? tradingPair.FormattedName : options.Pair;
                    _loggingService.Info($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Amount: {orderDetails.AmountFilled:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    _ = _notificationService.NotifyAsync($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    OrderHistory.Push(orderDetails);
                }
                else
                {
                    _loggingService.Info($"Unable to place buy order for {options.Pair}. Order result: {orderDetails?.Result}");
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
        public Task BuyAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            if (CanBuy(options, out string message))
            {
                return InitiateBuyAsync(options, cancellationToken);
            }
            else
            {
                _loggingService.Debug(message);
                return Task.CompletedTask;
            }
        }

        private async Task InitiateBuyAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.BuyTrailing != 0)
            {
                // Initiate trailing buy (synchronous operation)
                _trailingManager.InitiateTrailingBuy(options.Pair);
            }
            else
            {
                await PlaceBuyOrderAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IOrderDetails> PlaceBuyOrderAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            IOrderDetails orderDetails = null;
            _trailingManager.CancelTrailingBuy(options.Pair);
            _trailingManager.CancelTrailingSell(options.Pair);

            if (CanBuy(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = GetCurrentPrice(options.Pair);
                decimal amount = options.Amount ?? Math.Round(options.MaxCost.Value / currentPrice, 4);

                var orderRequest = new BuyOrder
                {
                    Type = pairConfig.BuyType,
                    Pair = options.Pair,
                    Amount = amount,
                    Price = currentPrice
                };

                // Use async order placement for non-blocking I/O
                orderDetails = await Account.PlaceOrderAsync(orderRequest, options.Metadata, cancellationToken).ConfigureAwait(false);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    string newPairName = tradingPair != null ? tradingPair.FormattedName : options.Pair;
                    _loggingService.Info($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Amount: {orderDetails.AmountFilled:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    _ = _notificationService.NotifyAsync($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    OrderHistory.Push(orderDetails);
                }
                else
                {
                    _loggingService.Info($"Unable to place buy order for {options.Pair}. Order result: {orderDetails?.Result}");
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
