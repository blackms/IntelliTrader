using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
        private HealthCheckTimedTask healthCheckTimedTask;

        public void Start()
        {
            loggingService.Info($"Start Health Check service...");

            var core = coreService.Value;
            healthCheckTimedTask = new HealthCheckTimedTask(loggingService, notificationService, this, core, tradingService.Value);
            healthCheckTimedTask.RunInterval = (float)(core.Config.HealthCheckInterval * 1000 / _applicationContext.Speed);
            healthCheckTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / _applicationContext.Speed;
            core.AddTask(nameof(HealthCheckTimedTask), healthCheckTimedTask);

            loggingService.Info("Health Check service started");
        }

        public void Stop()
        {
            loggingService.Info($"Stop Health Check service...");

            var core = coreService.Value;
            core.StopTask(nameof(HealthCheckTimedTask));
            core.RemoveTask(nameof(HealthCheckTimedTask));

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
