using IntelliTrader.Core;
using IntelliTrader.Signals.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Backtesting
{
    public class BacktestingSignalsService : ConfigrableServiceBase<SignalsConfig>, ISignalsService
    {
        public override string ServiceName => Constants.ServiceNames.SignalsService;

        ISignalsConfig ISignalsService.Config => Config;

        public IModuleRules Rules { get; private set; }
        public ISignalRulesConfig RulesConfig { get; private set; }

        private readonly ILoggingService loggingService;
        private readonly IHealthCheckService healthCheckService;
        private readonly ITradingService tradingService;
        private readonly IRulesService rulesService;
        private readonly IBacktestingService backtestingService;
        private readonly ICoreService coreService;

        // Note: Old SignalRulesTimedTask has been replaced by SignalRuleProcessorService in IntelliTrader.Infrastructure
        private readonly ConcurrentDictionary<string, bool> trailingSignals = new ConcurrentDictionary<string, bool>();
        private IEnumerable<string> signalNames;

        public BacktestingSignalsService(
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            ITradingService tradingService,
            IRulesService rulesService,
            IBacktestingService backtestingService,
            ICoreService coreService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            this.tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            this.rulesService = rulesService ?? throw new ArgumentNullException(nameof(rulesService));
            this.backtestingService = backtestingService ?? throw new ArgumentNullException(nameof(backtestingService));
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        }

        public void Start()
        {
            loggingService.Info("Start Backtesting Signals service...");

            OnSignalRulesChanged();
            rulesService.RegisterRulesChangeCallback(OnSignalRulesChanged);

            // Note: Signal rule processing is now handled by SignalRuleProcessorService
            // registered in the Infrastructure layer as a BackgroundService

            loggingService.Info("Backtesting Signals service started");
        }

        public void Stop()
        {
            loggingService.Info("Stop Backtesting Signals service...");

            // Note: BackgroundServices are stopped by the host when the application shuts down

            rulesService.UnregisterRulesChangeCallback(OnSignalRulesChanged);

            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.SignalRulesProcessed);

            loggingService.Info("Backtesting Signals service stopped");
        }

        public void ClearTrailing()
        {
            trailingSignals.Clear();
        }

        public List<string> GetTrailingSignals()
        {
            return trailingSignals.Keys.ToList();
        }

        public IEnumerable<ISignalTrailingInfo> GetTrailingInfo(string pair)
        {
            // Signal trailing info is now managed by TrailingManager in Application layer
            // Return empty list for backwards compatibility
            return Enumerable.Empty<ISignalTrailingInfo>();
        }

        public IEnumerable<string> GetSignalNames()
        {
            if (signalNames == null)
            {
                signalNames = backtestingService.GetCurrentSignals().Values.SelectMany(val => val.Select(s => s.Name)).Distinct().ToList();
            }
            return signalNames;

        }

        public IEnumerable<ISignal> GetAllSignals()
        {
            return GetSignalsByName(null);
        }

        public IEnumerable<ISignal> GetSignalsByName(string signalName)
        {
            IEnumerable<ISignal> allSignals = backtestingService.GetCurrentSignals().SelectMany(s => s.Value);
            if (signalName == null)
            {
                return allSignals;
            }
            else
            {
                return allSignals.Where(s => s.Name == signalName);
            }
        }

        public IEnumerable<ISignal> GetSignalsByPair(string pair)
        {
            if (backtestingService.GetCurrentSignals().TryGetValue(pair, out IEnumerable<ISignal> signalsByPair))
            {
                return signalsByPair;
            }
            else
            {
                return null;
            }
        }

        public ISignal GetSignal(string pair, string signalName)
        {
            return GetSignalsByName(signalName)?.FirstOrDefault(s => s.Pair == pair);
        }

        public double? GetRating(string pair, string signalName)
        {
            return GetSignalsByName(signalName)?.FirstOrDefault(s => s.Pair == pair)?.Rating;
        }

        public double? GetRating(string pair, IEnumerable<string> signalNames)
        {
            if (signalNames != null && signalNames.Count() > 0)
            {
                double ratingSum = 0;

                foreach (var signalName in signalNames)
                {
                    var rating = GetSignalsByName(signalName)?.FirstOrDefault(s => s.Pair == pair)?.Rating;
                    if (rating != null)
                    {
                        ratingSum += rating.Value;
                    }
                    else
                    {
                        return null;
                    }
                }

                return Math.Round(ratingSum / signalNames.Count(), 8);
            }
            else
            {
                return null;
            }
        }

        public double? GetGlobalRating()
        {
            try
            {
                double ratingSum = 0;
                double ratingCount = 0;

                var currentSignals = backtestingService.GetCurrentSignals();
                if (currentSignals != null)
                {
                    var signalGroups = currentSignals.Values.SelectMany(s => s).GroupBy(s => s.Name);
                    foreach (var signalGroup in signalGroups)
                    {
                        if (Config.GlobalRatingSignals.Contains(signalGroup.Key))
                        {
                            double? averageRating = signalGroup.Average(s => s.Rating);
                            if (averageRating != null)
                            {
                                ratingSum += averageRating.Value;
                                ratingCount++;
                            }
                        }
                    }
                }

                if (ratingCount > 0)
                {
                    return Math.Round(ratingSum / ratingCount, 8);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to get global rating", ex);
                return null;
            }
        }

        private void OnSignalRulesChanged()
        {
            Rules = rulesService.GetRules(ServiceName);
            RulesConfig = Rules.GetConfiguration<SignalRulesConfig>();
        }
    }
}
