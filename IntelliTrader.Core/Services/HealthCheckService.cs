using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace IntelliTrader.Core
{
    internal class HealthCheckService : IHealthCheckService
    {
        private readonly ILoggingService loggingService;
        private readonly INotificationService notificationService;
        private readonly ICoreService coreService;
        private readonly ITradingService tradingService;

        private readonly ConcurrentDictionary<string, HealthCheck> healthChecks = new ConcurrentDictionary<string, HealthCheck>();
        private HealthCheckTimedTask healthCheckTimedTask;

        public HealthCheckService(
            ILoggingService loggingService,
            INotificationService notificationService,
            ICoreService coreService,
            ITradingService tradingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            this.tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        }

        public void Start()
        {
            loggingService.Info($"Start Health Check service...");

            healthCheckTimedTask = new HealthCheckTimedTask(loggingService, notificationService, this, coreService, tradingService);
            healthCheckTimedTask.RunInterval = (float)(coreService.Config.HealthCheckInterval * 1000 / Application.Speed);
            healthCheckTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / Application.Speed;
            coreService.AddTask(nameof(HealthCheckTimedTask), healthCheckTimedTask);

            loggingService.Info("Health Check service started");
        }

        public void Stop()
        {
            loggingService.Info($"Stop Health Check service...");

            coreService.StopTask(nameof(HealthCheckTimedTask));
            coreService.RemoveTask(nameof(HealthCheckTimedTask));

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
