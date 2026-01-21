using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Orchestrator responsible for swap operations - selling one pair to buy another.
    /// Coordinates between buy and sell orchestrators to complete the swap transaction.
    /// </summary>
    internal class SwapOrchestrator : ISwapOrchestrator
    {
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly IBuyOrchestrator _buyOrchestrator;
        private readonly ISellOrchestrator _sellOrchestrator;
        private readonly Func<ITradingConfig> _configProvider;
        private readonly Func<string, IPairConfig> _pairConfigProvider;
        private readonly Func<ITradingAccount> _accountProvider;
        private readonly Func<IEnumerable<string>> _marketPairsProvider;
        private readonly Func<string, decimal> _priceProvider;

        public SwapOrchestrator(
            ILoggingService loggingService,
            INotificationService notificationService,
            IBuyOrchestrator buyOrchestrator,
            ISellOrchestrator sellOrchestrator,
            Func<ITradingConfig> configProvider,
            Func<string, IPairConfig> pairConfigProvider,
            Func<ITradingAccount> accountProvider,
            Func<IEnumerable<string>> marketPairsProvider,
            Func<string, decimal> priceProvider)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _buyOrchestrator = buyOrchestrator ?? throw new ArgumentNullException(nameof(buyOrchestrator));
            _sellOrchestrator = sellOrchestrator ?? throw new ArgumentNullException(nameof(sellOrchestrator));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _pairConfigProvider = pairConfigProvider ?? throw new ArgumentNullException(nameof(pairConfigProvider));
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _marketPairsProvider = marketPairsProvider ?? throw new ArgumentNullException(nameof(marketPairsProvider));
            _priceProvider = priceProvider ?? throw new ArgumentNullException(nameof(priceProvider));
        }

        private ITradingConfig Config => _configProvider();
        private ITradingAccount Account => _accountProvider();

        private IPairConfig GetPairConfig(string pair) => _pairConfigProvider(pair);
        private IEnumerable<string> GetMarketPairs() => _marketPairsProvider();
        private decimal GetCurrentPrice(string pair) => _priceProvider(pair);

        public void Swap(SwapOptions options)
        {
            if (!CanSwap(options, out string message))
            {
                _loggingService.Info(message);
                return;
            }

            ITradingPair oldTradingPair = Account.GetTradingPair(options.OldPair);
            var sellOptions = CreateSwapSellOptions(options);

            if (!_sellOrchestrator.CanSell(sellOptions, out message))
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}: {message}");
                return;
            }

            // Capture old pair state before selling
            decimal currentMargin = oldTradingPair.CurrentMargin;
            decimal additionalCosts = CalculateAdditionalCosts(oldTradingPair);
            int additionalDCALevels = oldTradingPair.DCALevel;

            IOrderDetails sellOrderDetails = _sellOrchestrator.PlaceSellOrder(sellOptions);

            // Verify sell was successful
            if (Account.HasTradingPair(options.OldPair))
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to sell {options.OldPair}");
                return;
            }

            // Execute buy for new pair
            var buyOptions = CreateSwapBuyOptions(options, sellOrderDetails, currentMargin, additionalCosts, additionalDCALevels);
            IOrderDetails buyOrderDetails = _buyOrchestrator.PlaceBuyOrder(buyOptions);

            var newTradingPair = Account.GetTradingPair(options.NewPair) as TradingPair;
            if (newTradingPair == null)
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to buy {options.NewPair}");
                _ = _notificationService.NotifyAsync($"Unable to swap {options.OldPair} for {options.NewPair}: Failed to buy {options.NewPair}");
                return;
            }

            // Add sell fees to new position's additional costs
            AddSwapFeesToPosition(newTradingPair, sellOrderDetails);
            _loggingService.Info($"Swap {oldTradingPair.FormattedName} for {newTradingPair.FormattedName}. Old margin: {oldTradingPair.CurrentMargin:0.00}, new margin: {newTradingPair.CurrentMargin:0.00}");
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

        // Async methods (preferred for new code)
        public async Task SwapAsync(SwapOptions options, CancellationToken cancellationToken = default)
        {
            if (!CanSwap(options, out string message))
            {
                _loggingService.Info(message);
                return;
            }

            ITradingPair oldTradingPair = Account.GetTradingPair(options.OldPair);
            var sellOptions = CreateSwapSellOptions(options);

            if (!_sellOrchestrator.CanSell(sellOptions, out message))
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}: {message}");
                return;
            }

            // Capture old pair state before selling
            decimal currentMargin = oldTradingPair.CurrentMargin;
            decimal additionalCosts = CalculateAdditionalCosts(oldTradingPair);
            int additionalDCALevels = oldTradingPair.DCALevel;

            // Use async sell order placement
            IOrderDetails sellOrderDetails = await _sellOrchestrator.PlaceSellOrderAsync(sellOptions, cancellationToken).ConfigureAwait(false);

            // Verify sell was successful
            if (Account.HasTradingPair(options.OldPair))
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to sell {options.OldPair}");
                return;
            }

            // Execute buy for new pair using async method
            var buyOptions = CreateSwapBuyOptions(options, sellOrderDetails, currentMargin, additionalCosts, additionalDCALevels);
            IOrderDetails buyOrderDetails = await _buyOrchestrator.PlaceBuyOrderAsync(buyOptions, cancellationToken).ConfigureAwait(false);

            var newTradingPair = Account.GetTradingPair(options.NewPair) as TradingPair;
            if (newTradingPair == null)
            {
                _loggingService.Info($"Unable to swap {options.OldPair} for {options.NewPair}. Reason: failed to buy {options.NewPair}");
                _ = _notificationService.NotifyAsync($"Unable to swap {options.OldPair} for {options.NewPair}: Failed to buy {options.NewPair}");
                return;
            }

            // Add sell fees to new position's additional costs
            AddSwapFeesToPosition(newTradingPair, sellOrderDetails);
            _loggingService.Info($"Swap {oldTradingPair.FormattedName} for {newTradingPair.FormattedName}. Old margin: {oldTradingPair.CurrentMargin:0.00}, new margin: {newTradingPair.CurrentMargin:0.00}");
        }
    }
}
