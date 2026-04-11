using IntelliTrader.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IntelliTrader.Signals.Base
{
    public class SignalsService(
        ICoreService coreService,
        ILoggingService loggingService,
        IHealthCheckService healthCheckService,
        ITradingService tradingService,
        IRulesService rulesService,
        Func<string, string, IConfigurationSection, ISignalReceiver> signalReceiverFactory,
        IConfigProvider configProvider) : ConfigrableServiceBase<SignalsConfig>(configProvider), ISignalsService
    {
        private readonly bool _dependenciesValidated = ValidateDependencies(
            coreService,
            loggingService,
            healthCheckService,
            tradingService,
            rulesService,
            signalReceiverFactory);

        private static bool ValidateDependencies(
            ICoreService coreService,
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            ITradingService tradingService,
            IRulesService rulesService,
            Func<string, string, IConfigurationSection, ISignalReceiver> signalReceiverFactory)
        {
            ArgumentNullException.ThrowIfNull(coreService);
            ArgumentNullException.ThrowIfNull(loggingService);
            ArgumentNullException.ThrowIfNull(healthCheckService);
            ArgumentNullException.ThrowIfNull(tradingService);
            ArgumentNullException.ThrowIfNull(rulesService);
            ArgumentNullException.ThrowIfNull(signalReceiverFactory);
            return true;
        }

        public override string ServiceName => Constants.ServiceNames.SignalsService;

        protected override ILoggingService LoggingService => loggingService;

        ISignalsConfig ISignalsService.Config => Config;

        public IModuleRules Rules { get; private set; }
        public ISignalRulesConfig RulesConfig { get; private set; }

        private ConcurrentDictionary<string, ISignalReceiver> signalReceivers = new ConcurrentDictionary<string, ISignalReceiver>();

        // Note: Old SignalRulesTimedTask has been replaced by SignalRuleProcessorService in IntelliTrader.Infrastructure
        // These collections are kept for UI display purposes
        private readonly ConcurrentDictionary<string, bool> trailingSignals = new ConcurrentDictionary<string, bool>();

        public void Start()
        {
            loggingService.Info("Start Signals service...");

            OnSignalRulesChanged();
            rulesService.RegisterRulesChangeCallback(OnSignalRulesChanged);

            signalReceivers.Clear();
            foreach (var definition in Config.Definitions)
            {
                var receiver = signalReceiverFactory(definition.Receiver, definition.Name, definition.Configuration);

                if (receiver != null)
                {
                    if (signalReceivers.TryAdd(definition.Name, receiver))
                    {
                        receiver.Start();
                    }
                    else
                    {
                        throw new Exception($"Duplicate signal definition: {definition.Name}");
                    }
                }
                else
                {
                    throw new Exception($"Signal receiver not found: {definition.Receiver}");
                }
            }

            // Note: Signal rule processing is now handled by SignalRuleProcessorService
            // registered in the Infrastructure layer as a BackgroundService

            loggingService.Info("Signals service started");
        }

        public void Stop()
        {
            loggingService.Info("Stop Signals service...");

            // Create snapshot of values to avoid issues during iteration
            var receivers = signalReceivers.Values.ToList();
            foreach (var receiver in receivers)
            {
                receiver.Stop();
            }
            signalReceivers.Clear();

            // Note: BackgroundServices are stopped by the host when the application shuts down

            rulesService.UnregisterRulesChangeCallback(OnSignalRulesChanged);

            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.SignalRulesProcessed);

            loggingService.Info("Signals service stopped");
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
            return signalReceivers.OrderBy(r => r.Value.GetPeriod()).Select(r => r.Key);
        }

        public IEnumerable<ISignal> GetAllSignals()
        {
            return GetSignalsByName(null);
        }

        public IEnumerable<ISignal> GetSignalsByName(string signalName)
        {
            IEnumerable<ISignal> signals = null;
            foreach (var kvp in signalReceivers.OrderBy(r => r.Value.GetPeriod()))
            {
                if (signalName == null || signalName == kvp.Key)
                {
                    ISignalReceiver receiver = kvp.Value;
                    if (signals == null)
                    {
                        signals = receiver.GetSignals();
                    }
                    else
                    {
                        signals = signals.Concat(receiver.GetSignals());
                    }
                }
            }
            return signals;
        }

        public IEnumerable<ISignal> GetSignalsByPair(string pair)
        {
            foreach (var receiver in signalReceivers.Values.OrderBy(r => r.GetPeriod()))
            {
                var signal = receiver.GetSignals().FirstOrDefault(s => s.Pair == pair);
                if (signal != null)
                {
                    yield return signal;
                }
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

                foreach (var kvp in signalReceivers)
                {
                    string signalName = kvp.Key;
                    if (Config.GlobalRatingSignals.Contains(signalName))
                    {
                        ISignalReceiver receiver = kvp.Value;
                        double? averageRating = receiver.GetAverageRating();
                        if (averageRating != null)
                        {
                            ratingSum += averageRating.Value;
                            ratingCount++;
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
