using IntelliTrader.Core;
using IntelliTrader.Domain.Events;
using IntelliTrader.Exchange.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Aliases for disambiguation
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;
using CoreOrderSide = IntelliTrader.Core.OrderSide;
using IDomainEventDispatcher = IntelliTrader.Application.Ports.Driven.IDomainEventDispatcher;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Facade service that coordinates trading operations through specialized orchestrators.
    /// Maintains backward compatibility while delegating to single-responsibility components.
    /// </summary>
    // DI cycle notes:
    //  * coreService was previously injected here but never used inside
    //    the class. It has been removed entirely to break the
    //    CoreService -> TradingService -> CoreService cycle.
    //  * backtestingService and signalsService are injected as Lazy<T>
    //    because TradingService is itself a constructor dependency of
    //    BacktestingService and SignalsService. The lazy wrapper defers
    //    resolution until the first runtime access (Start/Stop/order
    //    execution paths), by which point the container is fully built.
    //    See `IsReplayingSnapshots` below for the one place where this
    //    used to live in a field initializer.
    internal class TradingService(
        ILoggingService loggingService,
        INotificationService notificationService,
        IHealthCheckService healthCheckService,
        IRulesService rulesService,
        Lazy<IBacktestingService> backtestingService,
        Lazy<ISignalsService> signalsService,
        Func<string, IExchangeService> exchangeServiceFactory,
        IApplicationContext applicationContext,
        IConfigProvider configProvider,
        IPortfolioRiskManager portfolioRiskManager = null,
        IPositionSizerFactory positionSizerFactory = null,
        IDomainEventDispatcher domainEventDispatcher = null) : ConfigurableServiceBase<TradingConfig>(configProvider), ITradingService
    {
        private readonly IApplicationContext _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        private readonly IDomainEventDispatcher _domainEventDispatcher = domainEventDispatcher;
        private const int MIN_INTERVAL_BETWEEN_BUY_AND_SELL = 10000;

        // Track suspension time for TradingResumedEvent
        private DateTimeOffset? _suspendedAt;
        private SuspensionReason? _lastSuspensionReason;

        public override string ServiceName => Constants.ServiceNames.TradingService;

        protected override ILoggingService LoggingService => loggingService;

        ITradingConfig ITradingService.Config => Config;

        private readonly object _syncRoot = new object();
        public object SyncRoot => _syncRoot;

        public IModuleRules Rules { get; private set; }
        public TradingRulesConfig RulesConfig { get; private set; }

        public ITradingAccount Account { get; private set; }

        // Bounded order history - lazily initialized to use configured MaxOrderHistorySize
        private BoundedConcurrentStack<IOrderDetails> _orderHistory;
        public BoundedConcurrentStack<IOrderDetails> OrderHistory => _orderHistory ??= CreateOrderHistory();

        public bool IsTradingSuspended { get; private set; }

        // Note: Old TimedTasks (TradingTimedTask, TradingRulesTimedTask, AccountTimedTask) have been
        // replaced by BackgroundServices in IntelliTrader.Infrastructure:
        // - TradingRuleProcessorService
        // - OrderExecutionService
        // - SignalRuleProcessorService

        // Computed at first access (not in a field initializer) so we
        // do not force-resolve the lazy IBacktestingService at construct
        // time, which would re-introduce the DI cycle.
        private bool IsReplayingSnapshots
        {
            get
            {
                var backtesting = backtestingService?.Value;
                return backtesting?.Config.Enabled == true && backtesting.Config.Replay;
            }
        }
        private bool tradingForcefullySuspended;

        // Orchestrators for single-responsibility operations (lazily initialized)
        private ITrailingOrderManager _trailingManager;
        private IBuyOrchestrator _buyOrchestrator;
        private ISellOrchestrator _sellOrchestrator;
        private ISwapOrchestrator _swapOrchestrator;

        // Trailing order manager - public accessor for backward compatibility
        private ITrailingOrderManager TrailingManager => _trailingManager ??= new TrailingOrderManager(loggingService);

        // Buy orchestrator - lazily initialized
        private IBuyOrchestrator BuyOrchestrator => _buyOrchestrator ??= CreateBuyOrchestrator();

        // Sell orchestrator - lazily initialized
        private ISellOrchestrator SellOrchestrator => _sellOrchestrator ??= CreateSellOrchestrator();

        // Swap orchestrator - lazily initialized
        private ISwapOrchestrator SwapOrchestrator => _swapOrchestrator ??= CreateSwapOrchestrator();

        // Pair config cache
        private readonly ConcurrentDictionary<string, PairConfig> pairConfigs = new ConcurrentDictionary<string, PairConfig>();

        // Exchange service - lazily resolved to allow Config access
        private IExchangeService _exchangeService;
        private IExchangeService exchangeService => _exchangeService ??= ResolveExchangeService();

        // Position sizer - lazily resolved based on configuration
        private IPositionSizer _positionSizer;
        private IPositionSizer positionSizer => _positionSizer ??= ResolvePositionSizer();

        private IBuyOrchestrator CreateBuyOrchestrator()
        {
            return new BuyOrchestrator(
                loggingService,
                notificationService,
                TrailingManager,
                () => Config,
                GetPairConfig,
                () => Account,
                () => IsTradingSuspended,
                GetCurrentPrice,
                () => OrderHistory,
                ReapplyTradingRules,
                portfolioRiskManager);
        }

        private ISellOrchestrator CreateSellOrchestrator()
        {
            return new SellOrchestrator(
                loggingService,
                notificationService,
                TrailingManager,
                () => Config,
                GetPairConfig,
                () => Account,
                () => IsTradingSuspended,
                GetCurrentPrice,
                () => OrderHistory,
                ReapplyTradingRules,
                _applicationContext);
        }

        private ISwapOrchestrator CreateSwapOrchestrator()
        {
            return new SwapOrchestrator(
                loggingService,
                notificationService,
                BuyOrchestrator,
                SellOrchestrator,
                () => Config,
                GetPairConfig,
                () => Account,
                GetMarketPairs,
                GetCurrentPrice);
        }

        private IExchangeService ResolveExchangeService()
        {
            ArgumentNullException.ThrowIfNull(exchangeServiceFactory);
            ArgumentNullException.ThrowIfNull(backtestingService);

            var serviceName = IsReplayingSnapshots
                ? Constants.ServiceNames.BacktestingExchangeService
                : Config.Exchange;

            var service = exchangeServiceFactory(serviceName);
            return service ?? throw new Exception($"Unsupported exchange: {serviceName}");
        }

        private IPositionSizer ResolvePositionSizer()
        {
            if (positionSizerFactory == null)
            {
                loggingService.Debug("Position sizer factory not available, using default FixedPercentagePositionSizer");
                return new FixedPercentagePositionSizer(loggingService);
            }

            return positionSizerFactory.Create(Config.PositionSizing);
        }

        private BoundedConcurrentStack<IOrderDetails> CreateOrderHistory()
        {
            // Validate and clamp the configured max size to allowed range
            var configuredSize = Config.MaxOrderHistorySize;
            var maxSize = Math.Max(
                Constants.Trading.MinOrderHistorySize,
                Math.Min(configuredSize, Constants.Trading.MaxOrderHistorySize));

            if (configuredSize != maxSize)
            {
                loggingService.Info($"OrderHistory size clamped from {configuredSize} to {maxSize} (valid range: {Constants.Trading.MinOrderHistorySize}-{Constants.Trading.MaxOrderHistorySize})");
            }

            var orderHistory = new BoundedConcurrentStack<IOrderDetails>(maxSize);

            // Subscribe to archiving events for logging
            orderHistory.ItemsArchived += (sender, args) =>
            {
                loggingService.Debug($"OrderHistory trimmed: {args.ArchivedItems.Count} oldest orders archived (current count: {orderHistory.Count})");
            };

            return orderHistory;
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
                Account = new ExchangeAccount(loggingService, notificationService, healthCheckService, signalsService.Value, this);
            }
            else
            {
                Account = new VirtualAccount(loggingService, notificationService, healthCheckService, signalsService.Value, this);
            }

            // Initial account refresh
            Account.Refresh();

            if (signalsService.Value.Config.Enabled)
            {
                signalsService.Value.Start();
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

            if (signalsService.Value.Config.Enabled)
            {
                signalsService.Value.Stop();
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

                // Capture suspension duration before clearing state
                var suspensionDuration = _suspendedAt.HasValue
                    ? DateTimeOffset.UtcNow - _suspendedAt.Value
                    : TimeSpan.Zero;
                var previousReason = _lastSuspensionReason;

                IsTradingSuspended = false;
                _suspendedAt = null;

                // Raise TradingResumedEvent
                RaiseEventSafe(new TradingResumedEvent(
                    resumedBy: forced ? "Manual (Forced)" : "Manual",
                    wasForced: tradingForcefullySuspended,
                    suspensionDuration: suspensionDuration,
                    previousSuspensionReason: previousReason));

                _lastSuspensionReason = null;

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
                _suspendedAt = DateTimeOffset.UtcNow;
                _lastSuspensionReason = SuspensionReason.Manual;

                // Count open positions
                int openPositions = Account?.GetTradingPairs().Count() ?? 0;
                int pendingOrders = TrailingManager.GetTrailingBuys().Count + TrailingManager.GetTrailingSells().Count;

                // Raise TradingSuspendedEvent
                RaiseEventSafe(new TradingSuspendedEvent(
                    reason: SuspensionReason.Manual,
                    suspendedBy: forced ? "Manual (Forced)" : "Manual",
                    isForced: forced,
                    openPositions: openPositions,
                    pendingOrders: pendingOrders));

                // Note: BackgroundServices check IsTradingSuspended flag and pause automatically
                // Clear all trailing operations via the TrailingOrderManager
                TrailingManager.ClearAll();
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
                StopLossInternal = (Config.StopLoss as StopLossConfig)?.Clone() ?? new StopLossConfig(),
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
                return Config.DCALevels[dcaLevel].BuyMultiplier ?? 1;
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
                IRule rule = signalsService.Value.Rules.Entries.FirstOrDefault(r => r.Name == options.Metadata.SignalRule);

                ITradingPair swappedPair = Account.GetTradingPairs().OrderBy(p => p.CurrentMargin).FirstOrDefault(tradingPair =>
                {
                    IPairConfig pairConfig = GetPairConfig(tradingPair.Pair);
                    return pairConfig.SellEnabled && pairConfig.SwapEnabled && pairConfig.SwapSignalRules != null && pairConfig.SwapSignalRules.Contains(options.Metadata.SignalRule) &&
                           tradingPair.OrderDates != null && tradingPair.OrderDates.Any() &&
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
                // Initiate trailing buy via TrailingOrderManager
                TrailingManager.InitiateTrailingBuy(options.Pair);
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
                // Initiate trailing sell via TrailingOrderManager
                TrailingManager.InitiateTrailingSell(options.Pair);
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
                if (!CanSwap(options, out string message))
                {
                    loggingService.Info(message);
                    return;
                }

                ITradingPair oldTradingPair = Account.GetTradingPair(options.OldPair);
                var sellOptions = CreateSwapSellOptions(options);

                if (!CanSell(sellOptions, out message))
                {
                    loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}: {message}");
                    return;
                }

                // Capture old pair state before selling
                decimal currentMargin = oldTradingPair.CurrentMargin;
                decimal additionalCosts = CalculateAdditionalCosts(oldTradingPair);
                int additionalDCALevels = oldTradingPair.DCALevel;

                IOrderDetails sellOrderDetails = PlaceSellOrder(sellOptions);

                // Verify sell was successful
                if (Account.HasTradingPair(options.OldPair))
                {
                    loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to sell {options.OldPair}");
                    return;
                }

                // Execute buy for new pair
                var buyOptions = CreateSwapBuyOptions(options, sellOrderDetails, currentMargin, additionalCosts, additionalDCALevels);
                IOrderDetails buyOrderDetails = PlaceBuyOrder(buyOptions);

                var newTradingPair = Account.GetTradingPair(options.NewPair) as TradingPair;
                if (newTradingPair == null)
                {
                    loggingService.Error($"SWAP INCOMPLETE: Sold {options.OldPair} but failed to buy {options.NewPair}. Funds may be in limbo! Attempting to re-buy old pair as compensation.");
                    _ = notificationService.NotifyAsync($"SWAP INCOMPLETE: Sold {options.OldPair} but failed to buy {options.NewPair}. Attempting compensation re-buy of {options.OldPair}.");

                    // Compensation: attempt to re-buy the old pair with the sell proceeds
                    try
                    {
                        var compensationBuyOptions = new BuyOptions(options.OldPair)
                        {
                            ManualOrder = true,
                            MaxCost = sellOrderDetails.AverageCost,
                            Metadata = options.Metadata
                        };
                        IOrderDetails compensationOrder = PlaceBuyOrder(compensationBuyOptions);
                        if (Account.HasTradingPair(options.OldPair))
                        {
                            loggingService.Warning($"Swap compensation successful: re-bought {options.OldPair}");
                            _ = notificationService.NotifyAsync($"Swap compensation successful: re-bought {options.OldPair}");
                        }
                        else
                        {
                            loggingService.Error($"SWAP COMPENSATION FAILED: Could not re-buy {options.OldPair}. Manual intervention required! Sell proceeds: {sellOrderDetails.AverageCost:0.00000000}");
                            _ = notificationService.NotifyAsync($"CRITICAL: Swap compensation failed for {options.OldPair}. Manual intervention required!");
                        }
                    }
                    catch (Exception ex)
                    {
                        loggingService.Error($"SWAP COMPENSATION EXCEPTION for {options.OldPair}: {ex.Message}. Manual intervention required!");
                        _ = notificationService.NotifyAsync($"CRITICAL: Swap compensation exception for {options.OldPair}. Manual intervention required!");
                    }
                    return;
                }

                // Add sell fees to new position's additional costs
                AddSwapFeesToPosition(newTradingPair, sellOrderDetails);
                loggingService.Info($"Swap {oldTradingPair.FormattedName} for {newTradingPair.FormattedName}. Old margin: {oldTradingPair.CurrentMargin:0.00}, new margin: {newTradingPair.CurrentMargin:0.00}");
            }
        }

        // Async trading methods (preferred for new code)
        public async Task BuyAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            IRule rule = signalsService.Value.Rules.Entries.FirstOrDefault(r => r.Name == options.Metadata.SignalRule);

            ITradingPair swappedPair = Account.GetTradingPairs().OrderBy(p => p.CurrentMargin).FirstOrDefault(tradingPair =>
            {
                IPairConfig pairConfig = GetPairConfig(tradingPair.Pair);
                return pairConfig.SellEnabled && pairConfig.SwapEnabled && pairConfig.SwapSignalRules != null && pairConfig.SwapSignalRules.Contains(options.Metadata.SignalRule) &&
                       tradingPair.OrderDates != null && tradingPair.OrderDates.Any() &&
                       pairConfig.SwapTimeout < (DateTimeOffset.Now - tradingPair.OrderDates.Max()).TotalSeconds;
            });

            if (swappedPair != null)
            {
                await SwapAsync(new SwapOptions(swappedPair.Pair, options.Pair, options.Metadata), cancellationToken).ConfigureAwait(false);
            }
            else if (rule?.Action != Constants.SignalRuleActions.Swap)
            {
                if (CanBuy(options, out string message))
                {
                    await InitiateBuyAsync(options, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    loggingService.Debug(message);
                }
            }
        }

        private async Task InitiateBuyAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.BuyTrailing != 0)
            {
                // Initiate trailing buy via TrailingOrderManager (synchronous)
                TrailingManager.InitiateTrailingBuy(options.Pair);
            }
            else
            {
                await PlaceBuyOrderAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SellAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            if (CanSell(options, out string message))
            {
                await InitiateSellAsync(options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                loggingService.Debug(message);
            }
        }

        private async Task InitiateSellAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            IPairConfig pairConfig = GetPairConfig(options.Pair);

            if (!options.ManualOrder && !options.Swap && pairConfig.SellTrailing != 0)
            {
                // Initiate trailing sell via TrailingOrderManager (synchronous)
                TrailingManager.InitiateTrailingSell(options.Pair);
            }
            else
            {
                await PlaceSellOrderAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SwapAsync(SwapOptions options, CancellationToken cancellationToken = default)
        {
            if (!CanSwap(options, out string message))
            {
                loggingService.Info(message);
                return;
            }

            ITradingPair oldTradingPair = Account.GetTradingPair(options.OldPair);
            var sellOptions = CreateSwapSellOptions(options);

            if (!CanSell(sellOptions, out message))
            {
                loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}: {message}");
                return;
            }

            // Capture old pair state before selling
            decimal currentMargin = oldTradingPair.CurrentMargin;
            decimal additionalCosts = CalculateAdditionalCosts(oldTradingPair);
            int additionalDCALevels = oldTradingPair.DCALevel;

            IOrderDetails sellOrderDetails = await PlaceSellOrderAsync(sellOptions, cancellationToken).ConfigureAwait(false);

            // Verify sell was successful
            if (Account.HasTradingPair(options.OldPair))
            {
                loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to sell {options.OldPair}");
                return;
            }

            // Execute buy for new pair
            var buyOptions = CreateSwapBuyOptions(options, sellOrderDetails, currentMargin, additionalCosts, additionalDCALevels);
            IOrderDetails buyOrderDetails = await PlaceBuyOrderAsync(buyOptions, cancellationToken).ConfigureAwait(false);

            var newTradingPair = Account.GetTradingPair(options.NewPair) as TradingPair;
            if (newTradingPair == null)
            {
                loggingService.Error($"SWAP INCOMPLETE: Sold {options.OldPair} but failed to buy {options.NewPair}. Funds may be in limbo! Attempting to re-buy old pair as compensation.");
                _ = notificationService.NotifyAsync($"SWAP INCOMPLETE: Sold {options.OldPair} but failed to buy {options.NewPair}. Attempting compensation re-buy of {options.OldPair}.");

                // Compensation: attempt to re-buy the old pair with the sell proceeds
                try
                {
                    var compensationBuyOptions = new BuyOptions(options.OldPair)
                    {
                        ManualOrder = true,
                        MaxCost = sellOrderDetails.AverageCost,
                        Metadata = options.Metadata
                    };
                    IOrderDetails compensationOrder = await PlaceBuyOrderAsync(compensationBuyOptions, cancellationToken).ConfigureAwait(false);
                    if (Account.HasTradingPair(options.OldPair))
                    {
                        loggingService.Warning($"Swap compensation successful: re-bought {options.OldPair}");
                        _ = notificationService.NotifyAsync($"Swap compensation successful: re-bought {options.OldPair}");
                    }
                    else
                    {
                        loggingService.Error($"SWAP COMPENSATION FAILED: Could not re-buy {options.OldPair}. Manual intervention required! Sell proceeds: {sellOrderDetails.AverageCost:0.00000000}");
                        _ = notificationService.NotifyAsync($"CRITICAL: Swap compensation failed for {options.OldPair}. Manual intervention required!");
                    }
                }
                catch (Exception ex)
                {
                    loggingService.Error($"SWAP COMPENSATION EXCEPTION for {options.OldPair}: {ex.Message}. Manual intervention required!");
                    _ = notificationService.NotifyAsync($"CRITICAL: Swap compensation exception for {options.OldPair}. Manual intervention required!");
                }
                return;
            }

            // Add sell fees to new position's additional costs
            AddSwapFeesToPosition(newTradingPair, sellOrderDetails);
            loggingService.Info($"Swap {oldTradingPair.FormattedName} for {newTradingPair.FormattedName}. Old margin: {oldTradingPair.CurrentMargin:0.00}, new margin: {newTradingPair.CurrentMargin:0.00}");
        }

        private SellOptions CreateSwapSellOptions(SwapOptions options)
        {
            return new SellOptions(options.OldPair)
            {
                Swap = true,
                SwapPair = options.NewPair,
                ManualOrder = options.ManualOrder
            };
        }

        private decimal CalculateAdditionalCosts(ITradingPair tradingPair)
        {
            return tradingPair.AverageCostPaid - tradingPair.CurrentCost + (tradingPair.Metadata.AdditionalCosts ?? 0);
        }

        private BuyOptions CreateSwapBuyOptions(SwapOptions options, IOrderDetails sellOrderDetails, decimal currentMargin, decimal additionalCosts, int additionalDCALevels)
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
            return buyOptions;
        }

        private void AddSwapFeesToPosition(TradingPair tradingPair, IOrderDetails sellOrderDetails)
        {
            if (sellOrderDetails.Fees == 0 || sellOrderDetails.FeesCurrency == null)
            {
                return;
            }

            if (sellOrderDetails.FeesCurrency == Config.Market)
            {
                tradingPair.Metadata.AdditionalCosts += sellOrderDetails.Fees;
            }
            else
            {
                string feesPair = sellOrderDetails.FeesCurrency + Config.Market;
                tradingPair.Metadata.AdditionalCosts += GetCurrentPrice(feesPair) * sellOrderDetails.Fees;
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
            else if (!options.ManualOrder && !options.Swap && pairConfig.BuySamePairTimeout > 0 && OrderHistory.Any(h => h.Side == CoreOrderSide.Buy && h.Pair == options.Pair) &&
                (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds < pairConfig.BuySamePairTimeout)
            {
                var elapsedSeconds = (DateTimeOffset.Now - OrderHistory.Where(h => h.Pair == options.Pair).Max(h => h.Date)).TotalSeconds;
                message = $"Cancel buy request for {options.Pair}. Reason: buy same pair timeout (elapsed: {elapsedSeconds:0.#}, timeout: {pairConfig.BuySamePairTimeout:0.#})";
                return false;
            }
            // Portfolio risk management checks
            else if (!options.ManualOrder && !options.Swap && portfolioRiskManager != null && Config.RiskManagement?.Enabled == true)
            {
                // Check circuit breaker
                if (portfolioRiskManager.IsCircuitBreakerTriggered())
                {
                    message = $"Cancel buy request for {options.Pair}. Reason: circuit breaker triggered (drawdown limit exceeded)";
                    return false;
                }

                // Check daily loss limit
                if (portfolioRiskManager.IsDailyLossLimitReached())
                {
                    message = $"Cancel buy request for {options.Pair}. Reason: daily loss limit reached ({portfolioRiskManager.GetDailyProfitLoss():0.00}%)";
                    return false;
                }

                // Check portfolio heat
                decimal positionRisk = Config.RiskManagement.DefaultPositionRiskPercent;
                if (portfolioRiskManager is PortfolioRiskManager prm && options.MaxCost.HasValue)
                {
                    positionRisk = prm.CalculatePositionRisk(options.MaxCost.Value, Math.Abs(Config.SellStopLossMargin));
                }

                if (!portfolioRiskManager.CanOpenPosition(positionRisk))
                {
                    decimal currentHeat = portfolioRiskManager.GetCurrentHeat();
                    message = $"Cancel buy request for {options.Pair}. Reason: portfolio heat limit reached (current: {currentHeat:0.00}%, max: {Config.RiskManagement.MaxPortfolioHeat:0.00}%)";
                    return false;
                }
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
            else
            {
                var orderDates = Account.GetTradingPair(options.Pair).OrderDates;
                if (orderDates != null && orderDates.Any() &&
                    (DateTimeOffset.Now - orderDates.Max()).TotalMilliseconds < (MIN_INTERVAL_BETWEEN_BUY_AND_SELL / _applicationContext.Speed))
                {
                    message = $"Cancel sell request for {options.Pair}. Reason: pair just bought";
                    return false;
                }
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

        /// <summary>
        /// Calculates the position size for a buy order using the configured position sizing algorithm.
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="entryPrice">Expected entry price</param>
        /// <returns>Calculated position size in quote currency, or null if position sizing is disabled</returns>
        public decimal? CalculatePositionSize(string pair, decimal entryPrice)
        {
            var positionSizingConfig = Config.PositionSizing;
            if (positionSizingConfig == null || !positionSizingConfig.Enabled)
            {
                return null;
            }

            try
            {
                decimal accountBalance = Account.GetBalance();
                decimal riskPercent = positionSizingConfig.RiskPercent / 100m; // Convert from percentage to decimal
                decimal maxPositionPercent = positionSizingConfig.MaxPositionPercent / 100m;

                // Calculate stop loss price from default stop loss percentage
                decimal stopLossPercent = positionSizingConfig.DefaultStopLossPercent / 100m;
                decimal stopLossPrice = entryPrice * (1 - stopLossPercent);

                // Get historical trading statistics for Kelly Criterion
                var (winRate, avgWinLoss) = CalculateTradingStatistics(positionSizingConfig.MinTradesForStats);

                // Use configured values as fallback (convert from percentage to decimal)
                decimal? effectiveWinRate = winRate ?? (positionSizingConfig.WinRate.HasValue ? positionSizingConfig.WinRate.Value / 100m : null);
                decimal? effectiveAvgWinLoss = avgWinLoss ?? positionSizingConfig.AverageWinLoss;

                var context = new PositionSizingContext(
                    accountBalance: accountBalance,
                    riskPercent: riskPercent,
                    entryPrice: entryPrice,
                    stopLossPrice: stopLossPrice,
                    winRate: effectiveWinRate,
                    averageWinLoss: effectiveAvgWinLoss,
                    maxPositionPercent: maxPositionPercent,
                    pair: pair
                );

                var result = positionSizer.CalculatePositionSizeWithDetails(context);

                loggingService.Info($"Position sizing [{result.Method}] for {pair}: " +
                    $"Size={result.PositionSize:0.00000000}, " +
                    $"Risk={result.RiskAmount:0.00000000}, " +
                    $"Position%={result.PositionPercent * 100:0.00}%, " +
                    $"Capped={result.WasCapped}" +
                    (result.RawKellyPercent.HasValue ? $", RawKelly={result.RawKellyPercent.Value * 100:0.00}%" : ""));

                return result.PositionSize;
            }
            catch (Exception ex)
            {
                loggingService.Info($"Error calculating position size for {pair}: {ex.Message}. Using default BuyMaxCost.");
                return null;
            }
        }

        /// <summary>
        /// Calculates trading statistics from order history for Kelly Criterion.
        /// </summary>
        private (decimal? winRate, decimal? avgWinLoss) CalculateTradingStatistics(int minTrades)
        {
            try
            {
                // Get completed trades (matched buy/sell pairs)
                var completedTrades = new List<(decimal profit, decimal cost)>();

                // Group orders by pair to find completed round trips
                var ordersByPair = OrderHistory
                    .Where(o => o.Result == OrderResult.Filled)
                    .GroupBy(o => o.Pair);

                foreach (var pairOrders in ordersByPair)
                {
                    var buys = pairOrders.Where(o => o.Side == CoreOrderSide.Buy).OrderBy(o => o.Date).ToList();
                    var sells = pairOrders.Where(o => o.Side == CoreOrderSide.Sell).OrderBy(o => o.Date).ToList();

                    // Match buys with sells
                    int matchCount = Math.Min(buys.Count, sells.Count);
                    for (int i = 0; i < matchCount; i++)
                    {
                        decimal buyCost = buys[i].AverageCost;
                        decimal sellValue = sells[i].AverageCost;
                        decimal profit = sellValue - buyCost;
                        completedTrades.Add((profit, buyCost));
                    }
                }

                if (completedTrades.Count < minTrades)
                {
                    loggingService.Debug($"Not enough trades for statistics ({completedTrades.Count} < {minTrades})");
                    return (null, null);
                }

                // Calculate win rate
                int wins = completedTrades.Count(t => t.profit > 0);
                decimal winRate = (decimal)wins / completedTrades.Count;

                // Calculate average win/loss ratio
                var winningTrades = completedTrades.Where(t => t.profit > 0).ToList();
                var losingTrades = completedTrades.Where(t => t.profit < 0).ToList();

                if (winningTrades.Count == 0 || losingTrades.Count == 0)
                {
                    // Can't calculate ratio without both wins and losses
                    return (winRate, null);
                }

                decimal avgWin = winningTrades.Average(t => t.profit);
                decimal avgLoss = Math.Abs(losingTrades.Average(t => t.profit));
                decimal avgWinLoss = avgLoss > 0 ? avgWin / avgLoss : 1;

                loggingService.Debug($"Trading statistics: WinRate={winRate:P2}, AvgWin/Loss={avgWinLoss:0.00}, " +
                    $"Trades={completedTrades.Count}, Wins={wins}");

                return (winRate, avgWinLoss);
            }
            catch (Exception ex)
            {
                loggingService.Debug($"Error calculating trading statistics: {ex.Message}");
                return (null, null);
            }
        }

        private IOrderDetails PlaceBuyOrder(BuyOptions options)
        {
            // Snapshot config at method entry to prevent mid-operation config changes
            var config = Config;
            IOrderDetails orderDetails = null;
            TrailingManager.CancelTrailingBuy(options.Pair);
            TrailingManager.CancelTrailingSell(options.Pair);

            if (CanBuy(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = GetCurrentPrice(options.Pair);

                // Determine the max cost: use position sizing if enabled, otherwise use provided MaxCost
                decimal effectiveMaxCost;
                if (options.Amount.HasValue)
                {
                    // Amount specified directly, calculate cost
                    effectiveMaxCost = options.Amount.Value * currentPrice;
                }
                else if (config.PositionSizing?.Enabled == true && !options.ManualOrder && !options.Swap)
                {
                    // Use position sizing for automated trades
                    decimal? calculatedSize = CalculatePositionSize(options.Pair, currentPrice);
                    effectiveMaxCost = calculatedSize ?? options.MaxCost.Value;
                }
                else
                {
                    effectiveMaxCost = options.MaxCost.Value;
                }

                decimal amount = options.Amount ?? Math.Round(effectiveMaxCost / currentPrice, 4);

                var orderRequest = new BuyOrder
                {
                    Type = pairConfig.BuyType,
                    Pair = options.Pair,
                    Amount = amount,
                    Price = currentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, options.Metadata);

                // Raise OrderPlacedEvent
                if (orderDetails != null)
                {
                    RaiseEventSafe(new OrderPlacedEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Buy,
                        amount: amount,
                        price: currentPrice,
                        orderType: ToDomainOrderType(pairConfig.BuyType),
                        isManual: options.ManualOrder,
                        signalRule: options.Metadata?.SignalRule));
                }

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    string newPairName = tradingPair != null ? tradingPair.FormattedName : options.Pair;
                    loggingService.Info($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Amount: {orderDetails.AmountFilled:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    _ = notificationService.NotifyAsync($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    LogOrder(orderDetails);

                    // Raise OrderFilledEvent
                    RaiseEventSafe(new OrderFilledEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Buy,
                        filledAmount: orderDetails.AmountFilled,
                        averagePrice: orderDetails.AveragePrice,
                        cost: orderDetails.AverageCost,
                        fees: orderDetails.Fees));

                    // Register position with portfolio risk manager
                    if (portfolioRiskManager != null && config.RiskManagement?.Enabled == true)
                    {
                        decimal positionRisk = config.RiskManagement.DefaultPositionRiskPercent;
                        if (portfolioRiskManager is PortfolioRiskManager prm)
                        {
                            positionRisk = prm.CalculatePositionRisk(orderDetails.AverageCost, Math.Abs(config.SellStopLossMargin));
                        }
                        portfolioRiskManager.RegisterPosition(options.Pair, positionRisk);
                    }
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
            // Snapshot config at method entry to prevent mid-operation config changes
            var config = Config;
            IOrderDetails orderDetails = null;
            TrailingManager.CancelTrailingSell(options.Pair);
            TrailingManager.CancelTrailingBuy(options.Pair);

            // Capture the trading pair profit/loss before selling for risk management
            decimal profitLoss = 0m;

            if (CanSell(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                tradingPair.SetCurrentPrice(GetCurrentPrice(options.Pair));

                // Calculate profit/loss before the order is executed
                profitLoss = tradingPair.CurrentCost - tradingPair.AverageCostPaid;

                var orderRequest = new SellOrder
                {
                    Type = pairConfig.SellType,
                    Pair = options.Pair,
                    Amount = tradingPair.TotalAmount,
                    Price = tradingPair.CurrentPrice
                };

                orderDetails = Account.PlaceOrder(orderRequest, null);

                // Raise OrderPlacedEvent
                if (orderDetails != null)
                {
                    RaiseEventSafe(new OrderPlacedEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Sell,
                        amount: tradingPair.TotalAmount,
                        price: tradingPair.CurrentPrice,
                        orderType: ToDomainOrderType(pairConfig.SellType),
                        isManual: options.ManualOrder));
                }

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    loggingService.Info($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    _ = notificationService.NotifyAsync($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    LogOrder(orderDetails);

                    // Raise OrderFilledEvent
                    RaiseEventSafe(new OrderFilledEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Sell,
                        filledAmount: orderDetails.AmountFilled,
                        averagePrice: orderDetails.AveragePrice,
                        cost: orderDetails.AverageCost,
                        fees: orderDetails.Fees));

                    // Update portfolio risk manager
                    if (portfolioRiskManager != null && config.RiskManagement?.Enabled == true)
                    {
                        // Close the position in risk tracking
                        portfolioRiskManager.ClosePosition(options.Pair);

                        // Record the trade for daily P/L tracking
                        portfolioRiskManager.RecordTrade(profitLoss);

                        // Update peak equity if we made a profit
                        if (profitLoss > 0)
                        {
                            decimal currentEquity = Account.GetBalance();
                            foreach (var pair in Account.GetTradingPairs())
                            {
                                currentEquity += pair.CurrentCost;
                            }
                            portfolioRiskManager.UpdatePeakEquity(currentEquity);
                        }
                    }
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

        private async Task<IOrderDetails> PlaceBuyOrderAsync(BuyOptions options, CancellationToken cancellationToken = default)
        {
            // Snapshot config at method entry to prevent mid-operation config changes
            var config = Config;
            IOrderDetails orderDetails = null;
            TrailingManager.CancelTrailingBuy(options.Pair);
            TrailingManager.CancelTrailingSell(options.Pair);

            if (CanBuy(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = await GetCurrentPriceAsync(options.Pair).ConfigureAwait(false);

                // Determine the max cost: use position sizing if enabled, otherwise use provided MaxCost
                decimal effectiveMaxCost;
                if (options.Amount.HasValue)
                {
                    // Amount specified directly, calculate cost
                    effectiveMaxCost = options.Amount.Value * currentPrice;
                }
                else if (config.PositionSizing?.Enabled == true && !options.ManualOrder && !options.Swap)
                {
                    // Use position sizing for automated trades
                    decimal? calculatedSize = CalculatePositionSize(options.Pair, currentPrice);
                    effectiveMaxCost = calculatedSize ?? options.MaxCost.Value;
                }
                else
                {
                    effectiveMaxCost = options.MaxCost.Value;
                }

                decimal amount = options.Amount ?? Math.Round(effectiveMaxCost / currentPrice, 4);

                var orderRequest = new BuyOrder
                {
                    Type = pairConfig.BuyType,
                    Pair = options.Pair,
                    Amount = amount,
                    Price = currentPrice
                };

                // Use async order placement for non-blocking I/O
                orderDetails = await Account.PlaceOrderAsync(orderRequest, options.Metadata, cancellationToken).ConfigureAwait(false);

                // Raise OrderPlacedEvent
                if (orderDetails != null)
                {
                    RaiseEventSafe(new OrderPlacedEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Buy,
                        amount: amount,
                        price: currentPrice,
                        orderType: ToDomainOrderType(pairConfig.BuyType),
                        isManual: options.ManualOrder,
                        signalRule: options.Metadata?.SignalRule));
                }

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    string newPairName = tradingPair != null ? tradingPair.FormattedName : options.Pair;
                    loggingService.Info($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Amount: {orderDetails.AmountFilled:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    _ = notificationService.NotifyAsync($"Buy order filled for {newPairName}. Price: {orderDetails.AveragePrice:0.00000000}, Cost: {orderDetails.AverageCost:0.00}");
                    LogOrder(orderDetails);

                    // Raise OrderFilledEvent
                    RaiseEventSafe(new OrderFilledEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Buy,
                        filledAmount: orderDetails.AmountFilled,
                        averagePrice: orderDetails.AveragePrice,
                        cost: orderDetails.AverageCost,
                        fees: orderDetails.Fees));

                    // Register position with portfolio risk manager
                    if (portfolioRiskManager != null && config.RiskManagement?.Enabled == true)
                    {
                        decimal positionRisk = config.RiskManagement.DefaultPositionRiskPercent;
                        if (portfolioRiskManager is PortfolioRiskManager prm)
                        {
                            positionRisk = prm.CalculatePositionRisk(orderDetails.AverageCost, Math.Abs(config.SellStopLossMargin));
                        }
                        portfolioRiskManager.RegisterPosition(options.Pair, positionRisk);
                    }
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

        private async Task<IOrderDetails> PlaceSellOrderAsync(SellOptions options, CancellationToken cancellationToken = default)
        {
            // Snapshot config at method entry to prevent mid-operation config changes
            var config = Config;
            IOrderDetails orderDetails = null;
            TrailingManager.CancelTrailingSell(options.Pair);
            TrailingManager.CancelTrailingBuy(options.Pair);

            // Capture the trading pair profit/loss before selling for risk management
            decimal profitLoss = 0m;

            if (CanSell(options, out string message))
            {
                IPairConfig pairConfig = GetPairConfig(options.Pair);
                ITradingPair tradingPair = Account.GetTradingPair(options.Pair);
                decimal currentPrice = await GetCurrentPriceAsync(options.Pair).ConfigureAwait(false);
                tradingPair.SetCurrentPrice(currentPrice);

                // Calculate profit/loss before the order is executed
                profitLoss = tradingPair.CurrentCost - tradingPair.AverageCostPaid;

                var orderRequest = new SellOrder
                {
                    Type = pairConfig.SellType,
                    Pair = options.Pair,
                    Amount = tradingPair.TotalAmount,
                    Price = tradingPair.CurrentPrice
                };

                // Use async order placement for non-blocking I/O
                orderDetails = await Account.PlaceOrderAsync(orderRequest, null, cancellationToken).ConfigureAwait(false);

                // Raise OrderPlacedEvent
                if (orderDetails != null)
                {
                    RaiseEventSafe(new OrderPlacedEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Sell,
                        amount: tradingPair.TotalAmount,
                        price: tradingPair.CurrentPrice,
                        orderType: ToDomainOrderType(pairConfig.SellType),
                        isManual: options.ManualOrder));
                }

                if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
                {
                    loggingService.Info($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    _ = notificationService.NotifyAsync($"Sell order filled for {tradingPair.FormattedName}. Price: {orderDetails.AveragePrice:0.00000000}, Margin: {tradingPair.CurrentMargin:0.00}%");
                    LogOrder(orderDetails);

                    // Raise OrderFilledEvent
                    RaiseEventSafe(new OrderFilledEvent(
                        orderId: orderDetails.OrderId?.ToString() ?? Guid.NewGuid().ToString(),
                        pair: options.Pair,
                        side: DomainOrderSide.Sell,
                        filledAmount: orderDetails.AmountFilled,
                        averagePrice: orderDetails.AveragePrice,
                        cost: orderDetails.AverageCost,
                        fees: orderDetails.Fees));

                    // Update portfolio risk manager
                    if (portfolioRiskManager != null && config.RiskManagement?.Enabled == true)
                    {
                        // Close the position in risk tracking
                        portfolioRiskManager.ClosePosition(options.Pair);

                        // Record the trade for daily P/L tracking
                        portfolioRiskManager.RecordTrade(profitLoss);

                        // Update peak equity if we made a profit
                        if (profitLoss > 0)
                        {
                            decimal currentEquity = Account.GetBalance();
                            foreach (var pair in Account.GetTradingPairs())
                            {
                                currentEquity += pair.CurrentCost;
                            }
                            portfolioRiskManager.UpdatePeakEquity(currentEquity);
                        }
                    }
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
            return TrailingManager.GetTrailingBuys();
        }

        public List<string> GetTrailingSells()
        {
            return TrailingManager.GetTrailingSells();
        }

        public IEnumerable<ITicker> GetTickers()
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.GetTickers(Config.Market).GetAwaiter().GetResult();
        }

        public IEnumerable<string> GetMarketPairs()
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.GetMarketPairs(Config.Market).GetAwaiter().GetResult();
        }

        public Dictionary<string, decimal> GetAvailableAmounts()
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.GetAvailableAmounts().GetAwaiter().GetResult();
        }

        public IEnumerable<IOrderDetails> GetMyTrades(string pair)
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.GetMyTrades(pair).GetAwaiter().GetResult();
        }

        public IOrderDetails PlaceOrder(IOrder order)
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.PlaceOrder(order).GetAwaiter().GetResult();
        }

        public decimal GetCurrentPrice(string pair)
        {
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            return exchangeService.GetLastPrice(pair).GetAwaiter().GetResult();
        }

        // Async implementations - preferred for new code
        public async Task<IEnumerable<ITicker>> GetTickersAsync()
        {
            return await exchangeService.GetTickers(Config.Market).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> GetMarketPairsAsync()
        {
            return await exchangeService.GetMarketPairs(Config.Market).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, decimal>> GetAvailableAmountsAsync()
        {
            return await exchangeService.GetAvailableAmounts().ConfigureAwait(false);
        }

        public async Task<IEnumerable<IOrderDetails>> GetMyTradesAsync(string pair)
        {
            return await exchangeService.GetMyTrades(pair).ConfigureAwait(false);
        }

        public async Task<IOrderDetails> PlaceOrderAsync(IOrder order)
        {
            return await exchangeService.PlaceOrder(order).ConfigureAwait(false);
        }

        public async Task<decimal> GetCurrentPriceAsync(string pair)
        {
            return await exchangeService.GetLastPrice(pair).ConfigureAwait(false);
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

        /// <summary>
        /// Safely raises a domain event without blocking or throwing exceptions.
        /// If no dispatcher is configured, the event is silently dropped.
        /// </summary>
        /// <param name="domainEvent">The event to raise.</param>
        private void RaiseEventSafe(Domain.SharedKernel.IDomainEvent domainEvent)
        {
            if (_domainEventDispatcher == null)
            {
                return;
            }

            try
            {
                // Fire and forget - don't block trading operations for event dispatch
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _domainEventDispatcher.DispatchAsync(domainEvent).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        loggingService.Error($"Failed to dispatch domain event {domainEvent.GetType().Name}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                loggingService.Error($"Failed to queue domain event {domainEvent.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts Core OrderType to Domain OrderType.
        /// </summary>
        private static DomainOrderType ToDomainOrderType(Core.OrderType orderType)
        {
            return orderType switch
            {
                Core.OrderType.Market => DomainOrderType.Market,
                Core.OrderType.Limit => DomainOrderType.Limit,
                Core.OrderType.Stop => DomainOrderType.StopLoss,
                _ => DomainOrderType.Limit
            };
        }
    }
}
