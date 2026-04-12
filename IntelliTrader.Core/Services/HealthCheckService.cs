using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace IntelliTrader.Core
{
    // Both ICoreService and ITradingService are injected as Lazy<T> on
    // purpose: CoreService depends on IHealthCheckService and
    // TradingService depends on IHealthCheckService, so direct injection
    // would create cycles at container build time. Every use of these
    // services happens at Start()/Stop()/runtime methods, never inside
    // the constructor body, so deferring resolution via Lazy<T> is safe.
    internal class HealthCheckService(
        ILoggingService loggingService,
        INotificationService notificationService,
        Lazy<ICoreService> coreService,
        Lazy<ITradingService> tradingService,
        IApplicationContext applicationContext) : IHealthCheckService
    {
        private readonly IApplicationContext _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        private readonly ConcurrentDictionary<string, HealthCheck> healthChecks = new ConcurrentDictionary<string, HealthCheck>();
        private HealthCheckBackgroundService _backgroundService;
        private CancellationTokenSource _cts;

        public void Start()
        {
            loggingService.Info("Start Health Check service...");

            _cts = new CancellationTokenSource();
            _backgroundService = new HealthCheckBackgroundService(
                loggingService,
                notificationService,
                this,
                coreService,
                tradingService,
                _applicationContext);
            _backgroundService.StartAsync(_cts.Token);

            loggingService.Info("Health Check service started");
        }

        public void Stop()
        {
            loggingService.Info("Stop Health Check service...");

            if (_cts != null)
            {
                _cts.Cancel();
                _backgroundService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                _cts.Dispose();
                _cts = null;
                _backgroundService?.Dispose();
                _backgroundService = null;
            }

            loggingService.Info("Health Check service stopped");
        }

        public void UpdateHealthCheck(string name, string message = null, bool failed = false)
        {
            if (!healthChecks.TryGetValue(name, out HealthCheck existingHealthCheck))
            {
                healthChecks.TryAdd(name, new HealthCheck
                {
                    Name = name,
                    Message = message,
                    LastUpdated = DateTimeOffset.Now,
                    Failed = failed
                });
            }
            else
            {
                healthChecks[name].Message = message;
                healthChecks[name].LastUpdated = DateTimeOffset.Now;
                healthChecks[name].Failed = failed;
            }
        }

        public void RemoveHealthCheck(string name)
        {
            healthChecks.TryRemove(name, out HealthCheck healthCheck);
        }

        public IEnumerable<IHealthCheck> GetHealthChecks()
        {
            foreach (var kvp in healthChecks)
            {
                yield return kvp.Value;
            }
        }
    }
}
