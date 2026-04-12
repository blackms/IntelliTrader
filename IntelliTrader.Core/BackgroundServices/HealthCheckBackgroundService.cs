using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace IntelliTrader.Core
{
    /// <summary>
    /// BackgroundService replacement for HealthCheckTimedTask.
    /// Runs health-check logic on a configurable interval using the modern
    /// IHostedService / BackgroundService pattern instead of the legacy
    /// HighResolutionTimedTask.
    ///
    /// See ADR-0016 for migration strategy details.
    /// </summary>
    internal sealed class HealthCheckBackgroundService : BackgroundService
    {
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly Lazy<ICoreService> _coreService;
        private readonly Lazy<ITradingService> _tradingService;
        private readonly IApplicationContext _applicationContext;
        private int _healthCheckFailures;

        public HealthCheckBackgroundService(
            ILoggingService loggingService,
            INotificationService notificationService,
            IHealthCheckService healthCheckService,
            Lazy<ICoreService> coreService,
            Lazy<ITradingService> tradingService,
            IApplicationContext applicationContext)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait for CoreService to finish starting before we begin.
            await Task.Delay(TimeSpan.FromMilliseconds(
                Constants.TimedTasks.StandardDelay / _applicationContext.Speed), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var core = _coreService.Value;

                // Only run when the core service has started and health checks are enabled.
                if (core.Running && core.Config.HealthCheckEnabled)
                {
                    try
                    {
                        RunHealthCheck(core);
                    }
                    catch (Exception ex)
                    {
                        _loggingService.Error("Unhandled exception in HealthCheckBackgroundService", ex);
                    }
                }

                var intervalMs = core.Config.HealthCheckInterval * 1000 / _applicationContext.Speed;
                await Task.Delay(TimeSpan.FromMilliseconds(intervalMs), stoppingToken);
            }
        }

        private void RunHealthCheck(ICoreService coreService)
        {
            bool healthCheckFailed = false;
            _loggingService.Info("Health check results:");

            foreach (var healthCheck in _healthCheckService.GetHealthChecks().OrderBy(c => c.Name))
            {
                var elapsedSinceLastUpdate = (DateTimeOffset.Now - healthCheck.LastUpdated).TotalSeconds;
                bool healthCheckTimeout = coreService.Config.HealthCheckSuspendTradingTimeout > 0
                    && elapsedSinceLastUpdate > coreService.Config.HealthCheckSuspendTradingTimeout;
                string indicator = (healthCheck.Failed || healthCheckTimeout) ? "[-]" : "[+]";

                if (healthCheck.Message != null)
                {
                    _loggingService.Info($" {indicator} ({healthCheck.LastUpdated:HH:mm:ss}) {healthCheck.Name} - {healthCheck.Message}");
                }
                else
                {
                    _loggingService.Info($" {indicator} ({healthCheck.LastUpdated:HH:mm:ss}) {healthCheck.Name}");
                }

                if (healthCheck.Failed || healthCheckTimeout)
                {
                    healthCheckFailed = true;
                }
            }

            if (healthCheckFailed)
            {
                _healthCheckFailures++;
            }
            else
            {
                _healthCheckFailures = 0;
            }

            var tradingService = _tradingService.Value;

            if (healthCheckFailed
                && coreService.Config.HealthCheckFailuresToRestartServices > 0
                && _healthCheckFailures >= coreService.Config.HealthCheckFailuresToRestartServices)
            {
                coreService.Restart();
            }
            else
            {
                if (healthCheckFailed && !tradingService.IsTradingSuspended)
                {
                    _loggingService.Info($"Health check failed ({_healthCheckFailures})");
                    _ = _notificationService.NotifyAsync($"Health check failed ({_healthCheckFailures})");
                    _healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TradingPairsProcessed);
                    _healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TradingRulesProcessed);
                    _healthCheckService.RemoveHealthCheck(Constants.HealthChecks.SignalRulesProcessed);
                    tradingService.SuspendTrading();
                }
                else if (!healthCheckFailed && tradingService.IsTradingSuspended)
                {
                    _loggingService.Info("Health check passed");
                    _ = _notificationService.NotifyAsync("Health check passed");
                    tradingService.ResumeTrading();
                }
            }
        }
    }
}
