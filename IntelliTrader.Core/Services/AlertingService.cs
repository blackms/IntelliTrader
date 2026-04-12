using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Monitors system state and triggers notifications when alert conditions are met.
    /// Uses debouncing to avoid notification spam: each alert fires once when the condition
    /// becomes active, and once more when it resolves.
    /// </summary>
    internal class AlertingService : ConfigurableServiceBase<AlertingConfig>, IAlertingService
    {
        private readonly IHealthCheckService _healthCheckService;
        private readonly Lazy<ITradingService> _tradingService;
        private readonly INotificationService _notificationService;
        private readonly ILoggingService _loggingService;
        private readonly Lazy<ICoreService> _coreService;

        private AlertingTimedTask _alertingTimedTask;

        // Debounce state: tracks whether each alert condition is currently active
        private bool _tradingSuspendedAlerted;
        private readonly ConcurrentDictionary<string, int> _healthCheckFailureCounts = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, bool> _healthCheckAlerted = new ConcurrentDictionary<string, bool>();
        private bool _connectivityAlerted;
        private bool _signalStalenessAlerted;

        public override string ServiceName => Constants.ServiceNames.AlertingService;

        protected override ILoggingService LoggingService => _loggingService;

        public AlertingService(
            IHealthCheckService healthCheckService,
            Lazy<ITradingService> tradingService,
            INotificationService notificationService,
            ILoggingService loggingService,
            Lazy<ICoreService> coreService,
            IConfigProvider configProvider)
            : base(configProvider)
        {
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        }

        public void Start()
        {
            _loggingService.Info("Start Alerting service...");

            if (!Config.Enabled)
            {
                _loggingService.Info("Alerting service is disabled");
                return;
            }

            var core = _coreService.Value;
            _alertingTimedTask = new AlertingTimedTask(_loggingService, this);
            _alertingTimedTask.RunInterval = Config.CheckIntervalSeconds * 1000;
            _alertingTimedTask.StartDelay = Constants.TimedTasks.StandardDelay;
            core.AddTask(nameof(AlertingTimedTask), _alertingTimedTask);

            _loggingService.Info("Alerting service started");
        }

        public void Stop()
        {
            _loggingService.Info("Stop Alerting service...");

            if (_alertingTimedTask != null)
            {
                var core = _coreService.Value;
                core.StopTask(nameof(AlertingTimedTask));
                core.RemoveTask(nameof(AlertingTimedTask));
                _alertingTimedTask = null;
            }

            _loggingService.Info("Alerting service stopped");
        }

        public void CheckAlerts()
        {
            if (!Config.Enabled)
                return;

            try
            {
                CheckTradingSuspended();
                CheckHealthCheckFailures();
                CheckSignalStaleness();
                CheckConnectivity();
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error checking alerts", ex);
            }
        }

        private void CheckTradingSuspended()
        {
            if (!Config.TradingSuspendedAlert)
                return;

            try
            {
                var trading = _tradingService.Value;
                bool isSuspended = trading.IsTradingSuspended;

                if (isSuspended && !_tradingSuspendedAlerted)
                {
                    _tradingSuspendedAlerted = true;
                    SendAlert("ALERT: Trading has been suspended. Manual intervention may be required.");
                }
                else if (!isSuspended && _tradingSuspendedAlerted)
                {
                    _tradingSuspendedAlerted = false;
                    SendAlert("RESOLVED: Trading has been resumed.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error checking trading suspension alert", ex);
            }
        }

        private void CheckHealthCheckFailures()
        {
            try
            {
                var healthChecks = _healthCheckService.GetHealthChecks();
                var activeNames = new HashSet<string>();

                foreach (var hc in healthChecks)
                {
                    activeNames.Add(hc.Name);

                    if (hc.Failed)
                    {
                        int count = _healthCheckFailureCounts.AddOrUpdate(hc.Name, 1, (_, c) => c + 1);

                        if (count >= Config.HealthCheckFailureThreshold &&
                            !_healthCheckAlerted.GetOrAdd(hc.Name, false))
                        {
                            _healthCheckAlerted[hc.Name] = true;
                            SendAlert($"ALERT: Health check '{hc.Name}' has failed {count} consecutive times. Message: {hc.Message ?? "N/A"}");
                        }
                    }
                    else
                    {
                        // Reset failure count on success
                        if (_healthCheckFailureCounts.TryRemove(hc.Name, out _))
                        {
                            if (_healthCheckAlerted.TryGetValue(hc.Name, out bool wasAlerted) && wasAlerted)
                            {
                                _healthCheckAlerted[hc.Name] = false;
                                SendAlert($"RESOLVED: Health check '{hc.Name}' has recovered.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error checking health check failure alerts", ex);
            }
        }

        private void CheckSignalStaleness()
        {
            try
            {
                var healthChecks = _healthCheckService.GetHealthChecks();
                var signalCheck = healthChecks.FirstOrDefault(
                    hc => hc.Name == Constants.HealthChecks.TradingViewCryptoSignalsReceived);

                if (signalCheck == null)
                    return;

                var minutesSinceUpdate = (DateTimeOffset.Now - signalCheck.LastUpdated).TotalMinutes;
                bool isStale = minutesSinceUpdate > Config.SignalStalenessMinutes;

                if (isStale && !_signalStalenessAlerted)
                {
                    _signalStalenessAlerted = true;
                    SendAlert($"ALERT: Signal data is stale. Last update was {minutesSinceUpdate:F1} minutes ago (threshold: {Config.SignalStalenessMinutes} min).");
                }
                else if (!isStale && _signalStalenessAlerted)
                {
                    _signalStalenessAlerted = false;
                    SendAlert("RESOLVED: Signal data is being received again.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error checking signal staleness alert", ex);
            }
        }

        private void CheckConnectivity()
        {
            if (!Config.ConnectivityAlertEnabled)
                return;

            try
            {
                var healthChecks = _healthCheckService.GetHealthChecks();
                var tickerCheck = healthChecks.FirstOrDefault(
                    hc => hc.Name == Constants.HealthChecks.TickersUpdated);

                if (tickerCheck == null)
                    return;

                bool hasConnectivityIssue = tickerCheck.Failed;

                if (hasConnectivityIssue && !_connectivityAlerted)
                {
                    _connectivityAlerted = true;
                    SendAlert($"ALERT: Exchange connectivity issue detected. Tickers health check failed. Message: {tickerCheck.Message ?? "N/A"}");
                }
                else if (!hasConnectivityIssue && _connectivityAlerted)
                {
                    _connectivityAlerted = false;
                    SendAlert("RESOLVED: Exchange connectivity has been restored.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error checking connectivity alert", ex);
            }
        }

        private void SendAlert(string message)
        {
            _loggingService.Warning($"[Alerting] {message}");

            try
            {
                _ = _notificationService.NotifyAsync($"[Alert] {message}");
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to send alert notification", ex);
            }
        }

        protected override void OnConfigReloaded()
        {
            _loggingService.Info("Alerting configuration reloaded, restarting...");
            Stop();
            Start();
        }
    }
}
