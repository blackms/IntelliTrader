using IntelliTrader.Core;
using IntelliTrader.Signals.Base;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace IntelliTrader.Signals.TradingView
{
    internal class TradingViewCryptoSignalReceiver : ISignalReceiver
    {
        public string SignalName { get; private set; }
        public TradingViewCryptoSignalReceiverConfig Config { get; private set; }
        
        private readonly ILoggingService loggingService;
        private readonly IHealthCheckService healthCheckService;
        private readonly ISignalsService signalsService;
        private readonly ITradingService tradingService;
        private readonly ICoreService coreService;

        private TradingViewCryptoSignalPollingTimedTask tradingViewCryptoSignalPollingTimedTask;

        public TradingViewCryptoSignalReceiver(string signalName, IConfigurationSection configuration,
            ILoggingService loggingService, IHealthCheckService healthCheckService, ISignalsService signalsService, ITradingService tradingService, ICoreService coreService)
        {
            this.SignalName = signalName;
            this.Config = configuration.Get<TradingViewCryptoSignalReceiverConfig>();

            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            this.signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));
            this.tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        }

        public void Start()
        {
            loggingService.Info("Start TradingViewCryptoSignalReceiver...");

            tradingViewCryptoSignalPollingTimedTask = new TradingViewCryptoSignalPollingTimedTask(loggingService, healthCheckService, tradingService, this);
            tradingViewCryptoSignalPollingTimedTask.RunInterval = (float)(Config.PollingInterval * 1000 / Application.Speed);
            tradingViewCryptoSignalPollingTimedTask.Run();
            coreService.AddTask($"{nameof(TradingViewCryptoSignalPollingTimedTask)} [{SignalName}]", tradingViewCryptoSignalPollingTimedTask);

            loggingService.Info("TradingViewCryptoSignalReceiver started");
        }

        public void Stop()
        {
            loggingService.Info("Stop TradingViewCryptoSignalReceiver...");

            coreService.StopTask($"{nameof(TradingViewCryptoSignalPollingTimedTask)} [{SignalName}]");
            coreService.RemoveTask($"{nameof(TradingViewCryptoSignalPollingTimedTask)} [{SignalName}]");

            healthCheckService.RemoveHealthCheck($"{Constants.HealthChecks.TradingViewCryptoSignalsReceived} [{SignalName}]");

            loggingService.Info("TradingViewCryptoSignalReceiver stopped");
        }

        public int GetPeriod()
        {
            return Config.SignalPeriod;
        }

        public IEnumerable<ISignal> GetSignals()
        {
            return tradingViewCryptoSignalPollingTimedTask?.GetSignals() ?? new List<ISignal>();
        }

        public double? GetAverageRating()
        {
            return tradingViewCryptoSignalPollingTimedTask?.GetAverageRating();
        }
    }
}
