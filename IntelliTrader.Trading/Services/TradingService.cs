using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IntelliTrader.Trading
{
    internal class TradingService : ConfigrableServiceBase<TradingConfig>, ITradingService
    {
        private const int MIN_INTERVAL_BETWEEN_BUY_AND_SELL = 10000;

        public override string ServiceName => Constants.ServiceNames.TradingService;

        ITradingConfig ITradingService.Config => Config;

        public object SyncRoot { get; private set; } = new object();

        public IModuleRules Rules { get; private set; }
        public TradingRulesConfig RulesConfig { get; private set; }

        public ITradingAccount Account { get; private set; }
        public ConcurrentStack<IOrderDetails> OrderHistory { get; private set; } = new ConcurrentStack<IOrderDetails>();
        public bool IsTradingSuspended { get; private set; }

        private readonly ICoreService coreService;
        private readonly ILoggingService loggingService;
        private readonly INotificationService notificationService;
        private readonly IHealthCheckService healthCheckService;
        private readonly IRulesService rulesService;
        private readonly IBacktestingService backtestingService;
        private readonly IExchangeService exchangeService;
        private readonly ISignalsService signalsService;

        // Note: Old TimedTasks (TradingTimedTask, TradingRulesTimedTask, AccountTimedTask) have been
        // replaced by BackgroundServices in IntelliTrader.Infrastructure:
        // - TradingRuleProcessorService
        // - OrderExecutionService
        // - SignalRuleProcessorService

        private bool isReplayingSnapshots;
        private bool tradingForcefullySuspended;

        // Trailing buy/sell tracking (used for UI display)
        // Actual trailing logic is handled by TrailingManager in Application layer
        private readonly ConcurrentDictionary<string, bool> trailingBuys = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, bool> trailingSells = new ConcurrentDictionary<string, bool>();

        // Pair config cache
        private readonly ConcurrentDictionary<string, PairConfig> pairConfigs = new ConcurrentDictionary<string, PairConfig>();

        public TradingService(
            ICoreService coreService,
            ILoggingService loggingService,
            INotificationService notificationService,
            IHealthCheckService healthCheckService,
            IRulesService rulesService,
            IBacktestingService backtestingService,
            ISignalsService signalsService,
            Func<string, IExchangeService> exchangeServiceFactory)
        {
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            this.healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            this.rulesService = rulesService ?? throw new ArgumentNullException(nameof(rulesService));
            this.backtestingService = backtestingService ?? throw new ArgumentNullException(nameof(backtestingService));
            this.signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));

            this.isReplayingSnapshots = backtestingService.Config.Enabled && backtestingService.Config.Replay;

            if (isReplayingSnapshots)
            {
                this.exchangeService = exchangeServiceFactory(Constants.ServiceNames.BacktestingExchangeService);
            }
            else
            {
                this.exchangeService = exchangeServiceFactory(Config.Exchange);
            }

            if (this.exchangeService == null)
            {
                throw new Exception($"Unsupported exchange: {Config.Exchange}");
            }
        }

        public void Start()
        {
            loggingService.Info($"Start Trading service (Virtual: {Config.VirtualTrading})...");

            IsTradingSuspended = true;

            OnTradingRulesChanged();
            rulesService.RegisterRulesChangeCallback(OnTradingRulesChanged);

            exchangeService.Start(Config.VirtualTrading);

            if (!Config.VirtualTrading)
            {
                Account = new ExchangeAccount(loggingService, notificationService, healthCheckService, signalsService, this);
            }
            else
            {
                Account = new VirtualAccount(loggingService, notificationService, healthCheckService, signalsService, this);
            }

            // Initial account refresh
            Account.Refresh();

            if (signalsService.Config.Enabled)
            {
                signalsService.Start();
            }

            // Note: Trading rules and order execution are now handled by BackgroundServices
            // registered in the Infrastructure layer (TradingRuleProcessorService, OrderExecutionService)

            IsTradingSuspended = false;

            loggingService.Info("Trading service started");
        }

        public void Stop()
        {
            loggingService.Info("Stop Trading service...");

            exchangeService.Stop();

            if (signalsService.Config.Enabled)
            {
                signalsService.Stop();
            }

            // Note: BackgroundServices are stopped by the host when the application shuts down

            Account.Dispose();

            rulesService.UnregisterRulesChangeCallback(OnTradingRulesChanged);

            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TradingRulesProcessed);
            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TradingPairsProcessed);

            loggingService.Info("Trading service stopped");
        }

        public void ResumeTrading(bool forced)
        {
            if (IsTradingSuspended && (!tradingForcefullySuspended || forced))
            {
                loggingService.Info("Trading started");
                IsTradingSuspended = false;
                // BackgroundServices will automatically pick up trading when IsTradingSuspended is false
            }
        }

        public void SuspendTrading(bool forced)
        {
            if (!IsTradingSuspended)
            {
                loggingService.Info("Trading suspended");
                IsTradingSuspended = true;
                tradingForcefullySuspended = forced;

                // Note: BackgroundServices check IsTradingSuspended flag and pause automatically
                // Trailing is now managed by TrailingManager in the Application layer
                trailingBuys.Clear();
                trailingSells.Clear();
            }
        }

        public IPairConfig GetPairConfig(string pair)
        {
            // Get or create pair config from rules
            return pairConfigs.GetOrAdd(pair, p => CreateDefaultPairConfig(p));
        }

        public void ReapplyTradingRules()
        {
            // Clear pair config cache to force recomputation
            pairConfigs.Clear();
            // Note: TradingRuleProcessorService in Infrastructure layer handles continuous rule processing
        }

        private PairConfig CreateDefaultPairConfig(string pair)
        {
            // Create default pair config from trading rules config
            var tradingPair = Account?.GetTradingPair(pair);
            int dcaLevel = tradingPair?.DCALevel ?? 0;

            return new PairConfig
            {
                Rules = new List<string>(),
                BuyEnabled = RulesConfig?.BuyEnabled ?? true,
                BuyType = Config.BuyType,
                BuyMaxCost = Config.BuyMaxCost,
                BuyMultiplier = GetDCAMultiplier(dcaLevel),
                BuyMinBalance = RulesConfig?.BuyMinBalance ?? 0,
                BuySamePairTimeout = RulesConfig?.BuySamePairTimeout ?? 0,
                BuyTrailing = RulesConfig?.BuyTrailing ?? 0,
                BuyTrailingStopMargin = RulesConfig?.BuyTrailingStopMargin ?? 0,
                BuyTrailingStopAction = RulesConfig?.BuyTrailingStopAction ?? BuyTrailingStopAction.Cancel,
                SellEnabled = RulesConfig?.SellEnabled ?? true,
                SellType = Config.SellType,
                SellMargin = RulesConfig?.SellMargin ?? 1,
                SellTrailing = RulesConfig?.SellTrailing ?? 0,
                SellTrailingStopMargin = RulesConfig?.SellTrailingStopMargin ?? 0,
                SellTrailingStopAction = RulesConfig?.SellTrailingStopAction ?? SellTrailingStopAction.Cancel,
                SellStopLossEnabled = RulesConfig?.SellStopLossEnabled ?? false,
                SellStopLossAfterDCA = RulesConfig?.SellStopLossAfterDCA ?? false,
                SellStopLossMinAge = RulesConfig?.SellStopLossMinAge ?? 0,
                SellStopLossMargin = RulesConfig?.SellStopLossMargin ?? -10,
                SwapEnabled = RulesConfig?.SwapEnabled ?? false,
                SwapSignalRules = RulesConfig?.SwapSignalRules,
                SwapTimeout = RulesConfig?.SwapTimeout ?? 0,
                CurrentDCAMargin = GetCurrentDCAMargin(dcaLevel),
                NextDCAMargin = GetNextDCAMargin(dcaLevel)
            };
        }

        private decimal GetDCAMultiplier(int dcaLevel)
        {
            if (Config.DCALevels != null && dcaLevel < Config.DCALevels.Count)
            {
                return Config.DCALevels[dcaLevel].Multiplier;
            }
            return 1;
        }

        private decimal? GetCurrentDCAMargin(int dcaLevel)
        {
            if (dcaLevel == 0) return null;
            if (Config.DCALevels != null && dcaLevel - 1 < Config.DCALevels.Count)
            {
                return Config.DCALevels[dcaLevel - 1].Margin;
            }
            return null;
        }

        private decimal? GetNextDCAMargin(int dcaLevel)
        {
            if (Config.DCALevels != null && dcaLevel < Config.DCALevels.Count)
            {
                return Config.DCALevels[dcaLevel].Margin;
            }
            return null;
        }

        public void Buy(BuyOptions options)
        {
            lock (SyncRoot)
            {
                IRule rule = signalsService.Rules.Entries.FirstOrDefault(r => r.Name == options.Metadata.SignalRule);

                ITradingPair swappedPair = Account.GetTradingPairs().OrderBy(p => p.CurrentMargin).FirstOrDefault(tradingPair =>
                {
                    IPairConfig pairConfig = GetPairConfig(tradingPair.Pair);
                    return pairConfig.SellEnabled && pairConfig.SwapEnabled && pairConfig.SwapSignalRules != null && pairConfig.SwapSignalRules.Contains(options.Metadata.SignalRule) &&
                           pairConfig.SwapTimeout < (DateTimeOffset.Now - tradingPair.OrderDates.Max()).TotalSeconds;
                });

                if (swappedPair != null)
                {
                    Swap(new SwapOptions(swappedPair.Pair, options.Pair, options.Metadata));
                }
                else if (rule?.Action != Constants.SignalRuleActions.Swap)
                {
                    if (CanBuy(options, out string message))
                    {
                        InitiateBuy(options);
                    }
                    else
                    {
                        loggingService.Debug(message);
                    }
                }
            }
        }

        private void InitiateBuy(BuyOptions options)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.BuyTrailing != 0)
            {
                // Initiate trailing buy
                if (!trailingBuys.ContainsKey(options.Pair))
                {
                    trailingSells.TryRemove(options.Pair, out _);
                    trailingBuys.TryAdd(options.Pair, true);
                    loggingService.Info($"Trailing buy initiated for {options.Pair}");
                }
            }
            else
            {
                PlaceBuyOrder(options);
            }
        }

        public void Sell(SellOptions options)
        {
            lock (SyncRoot)
            {
                if (CanSell(options, out string message))
                {
                    InitiateSell(options);
                }
                else
                {
                    loggingService.Debug(message);
                }
            }
        }

        private void InitiateSell(SellOptions options)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.SellTrailing != 0)
            {
                // Initiate trailing sell
                if (!trailingSells.ContainsKey(options.Pair))
                {
                    trailingBuys.TryRemove(options.Pair, out _);
                    trailingSells.TryAdd(options.Pair, true);
                    loggingService.Info($"Trailing sell initiated for {options.Pair}");
                }
            }
            else
            {
                PlaceSellOrder(options);
            }
        }

        public void Swap(SwapOptions options)
        {
            lock (SyncRoot)
            {
                if (CanSwap(options, out string message))
                {
                    ITradingPair oldTradingPair = Account.GetTradingPair(options.OldPair);

                    var sellOptions = new SellOptions(options.OldPair)
                    {
                        Swap = true,
                        SwapPair = options.NewPair,
                        ManualOrder = options.ManualOrder
                    };

                    if (CanSell(sellOptions, out message))
                    {
                        decimal currentMargin = oldTradingPair.CurrentMargin;
                        decimal additionalCosts = oldTradingPair.AverageCostPaid - oldTradingPair.CurrentCost + (oldTradingPair.Metadata.AdditionalCosts ?? 0);
                        int additionalDCALevels = oldTradingPair.DCALevel;

                        IOrderDetails sellOrderDetails = PlaceSellOrder(sellOptions);
                        if (!Account.HasTradingPair(options.OldPair))
                        {
                            var buyOptions = new BuyOptions(options.NewPair)
                            {
                                Swap = true,
                                ManualOrder = options.ManualOrder,
                                MaxCost = sellOrderDetails.AverageCost,
                                Metadata = options.Metadata
                            };
                            buyOptions.Metadata.LastBuyMargin = currentMargin;
                            buyOptions.Metadata.SwapPair = options.OldPair;
                            buyOptions.Metadata.AdditionalDCALevels = additionalDCALevels;
                            buyOptions.Metadata.AdditionalCosts = additionalCosts;
                            IOrderDetails buyOrderDetails = PlaceBuyOrder(buyOptions);

                            var newTradingPair = Account.GetTradingPair(options.NewPair) as TradingPair;
                            if (newTradingPair != null)
                            {
                                if (sellOrderDetails.Fees != 0 && sellOrderDetails.FeesCurrency != null)
                                {
                                    if (sellOrderDetails.FeesCurrency == Config.Market)
                                    {
                                        newTradingPair.Metadata.AdditionalCosts += sellOrderDetails.Fees;
                                    }
                                    else
                                    {
                                        string feesPair = sellOrderDetails.FeesCurrency + Config.Market;
                                        newTradingPair.Metadata.AdditionalCosts += GetCurrentPrice(feesPair) * sellOrderDetails.Fees;
                                    }
                                }
                                loggingService.Info($"Swap {oldTradingPair.FormattedName} for {newTradingPair.FormattedName}. Old margin: {oldTradingPair.CurrentMargin:0.00}, new margin: {newTradingPair.CurrentMargin:0.00}");
                            }
                            else
                            {
                                loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to buy {options.NewPair}");
                                notificationService.Notify($"Unable to swap {options.OldPair} for {options.NewPair}: Failed to buy {options.NewPair}");
                            }
                        }
                        else
                        {
                            loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to sell {options.OldPair}");
                        }
                    }
                    else
                    {
                        loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}: {message}");
                    }
                }
                else
                {
                    loggingService.Info(message);
                }
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
            }
            else if (!options.ManualOrder && !options.Swap && pairConfig.BuySamePairTimeout > 0 && OrderHistory.Any(h => h.Side == OrderSide.Buy && h.Pair == options.Pair) &&
                (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds < pairConfig.BuySamePairTimeout)
            {
                var elapsedSeconds = (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds;
                message = $"Cancel buy request for {options.Pair}. Reason: buy same pair timeout (elapsed: {elapsedSeconds:0.#}, timeout: {pairConfig.BuySamePairTimeout:0.#})";
                return false;
            }

            message = null;
            return true;
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
            else if ((DateTimeOffset.Now - Account.GetTradingPair(options.Pair).OrderDates.Max()).TotalMilliseconds < (MIN_INTERVAL_BETWEEN_BUY_AND_SELL / Application.Speed))
            {
                message = $"Cancel sell request for {options.Pair}. Reason: pair just bought";
                return false;
            }
            message = null;
            return true;
        }

        public bool CanSwap(SwapOptions options, out string message)
        {
            if (!Account.HasTradingPair(options.OldPair))
            {
                message = $"Cancel swap request {options.OldPair} for {options.NewPair}. Reason: pair does not exist";
                return false;
            }
            else if (Account.HasTradingPair(options.NewPair))
            {
                message = $"Cancel swap request {options.OldPair} for {options.NewPair}. Reason: pair already exists";
                return false;
            }
            else if (!options.ManualOrder && !GetPairConfig(options.OldPair).SellEnabled)
            {
                message = $"Cancel swap request {options.OldPair} for {options.NewPair}. Reason: selling not enabled";
                return false;
            }
            else if (!options.ManualOrder && !GetPairConfig(options.NewPair).BuyEnabled)
            {
                message = $"Cancel swap request {options.OldPair} for {options.NewPair}. Reason: buying not enabled";
                return false;
            }
            else if (!GetMarketPairs().Contains(options.NewPair))
            {
                message = $"Cancel swap request {options.OldPair} for {options.NewPair}. Reason: {options.NewPair} is not a valid pair";
                return false;
            }

            message = null;
            return true;
        }

        public void LogOrder(IOrderDetails order)
        {
            OrderHistory.Push(order);
        }

        private IOrderDetails PlaceBuyOrder(BuyOptions options)
        {
            IOrderDetails orderDetails = null;
            trailingBuys.TryRemove(options.Pair, out _);
            trailingSells.TryRemove(options.Pair, out _);

            if (CanBuy(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = GetCurrentPrice(options.Pair);
                decimal amount = options.Amount ?? Math.Round(options.MaxCost.Value / currentPrice, 4);

                var orderRequest = new Order
                {
                    Type = pairConfig.BuyType,
                    Side = OrderSide.Buy,
                    Pair = options.Pair,
                    Amount = amount,
                    Price = currentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, options.Metadata);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    string newPairName = tradingPair != null ? tradingPair.FormattedName : options.Pair;
                    loggingService.Info($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Amount: {orderDetails.AmountFilled:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    notificationService.Notify($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    LogOrder(orderDetails);
                }
                else
                {
                    loggingService.Info($"Unable to place buy order for {options.Pair}. Order result: {orderDetails?.Result}");
                }

                ReapplyTradingRules();
            }
            else
            {
                loggingService.Info(message);
            }
            return orderDetails;
        }

        private IOrderDetails PlaceSellOrder(SellOptions options)
        {
            IOrderDetails orderDetails = null;
            trailingSells.TryRemove(options.Pair, out _);
            trailingBuys.TryRemove(options.Pair, out _);

            if (CanSell(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                tradingPair.SetCurrentPrice(GetCurrentPrice(options.Pair));

                var orderRequest = new Order
                {
                    Type = pairConfig.SellType,
                    Side = OrderSide.Sell,
                    Pair = options.Pair,
                    Amount = tradingPair.TotalAmount,
                    Price = tradingPair.CurrentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, null);

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    loggingService.Info($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    notificationService.Notify($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    LogOrder(orderDetails);
                }
                else
                {
                    loggingService.Info($"Unable to place sell order for {options.Pair}. Order result: {orderDetails?.Result}");
                }

                ReapplyTradingRules();
            }
            else
            {
                loggingService.Info(message);
            }
            return orderDetails;
        }

        public List<string> GetTrailingBuys()
        {
            return trailingBuys.Keys.ToList();
        }

        public List<string> GetTrailingSells()
        {
            return trailingSells.Keys.ToList();
        }

        public IEnumerable<ITicker> GetTickers()
        {
            return exchangeService.GetTickers(Config.Market).Result;
        }

        public IEnumerable<string> GetMarketPairs()
        {
            return exchangeService.GetMarketPairs(Config.Market).Result;
        }

        public Dictionary<string, decimal> GetAvailableAmounts()
        {
            return exchangeService.GetAvailableAmounts().Result;
        }

        public IEnumerable<IOrderDetails> GetMyTrades(string pair)
        {
            return exchangeService.GetMyTrades(pair).Result;
        }

        public IOrderDetails PlaceOrder(IOrder order)
        {
            return exchangeService.PlaceOrder(order).Result;
        }

        public decimal GetCurrentPrice(string pair)
        {
            return exchangeService.GetLastPrice(pair).Result;
        }

        private void OnTradingRulesChanged()
        {
            Rules = rulesService.GetRules(ServiceName);
            RulesConfig = Rules.GetConfiguration<TradingRulesConfig>();
        }

        protected override void PrepareConfig()
        {
            if (Config.ExcludedPairs == null)
            {
                Config.ExcludedPairs = new List<string>();
            }

            if (Config.DCALevels == null)
            {
                Config.DCALevels = new List<DCALevel>();
            }
        }
    }
}
