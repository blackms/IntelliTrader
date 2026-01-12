using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    internal class CoreService : ConfigrableServiceBase<CoreConfig>, ICoreService
    {
        public override string ServiceName => Constants.ServiceNames.CoreService;

        ICoreConfig ICoreService.Config => Config;

        public string Version { get; private set; }

        public bool Running { get; private set; }

        private readonly ILoggingService loggingService;
        private readonly INotificationService notificationService;
        private readonly IHealthCheckService healthCheckService;
        private readonly ITradingService tradingService;
        private readonly IWebService webService;
        private readonly IBacktestingService backtestingService;

        private ConcurrentDictionary<string, HighResolutionTimedTask> timedTasks;

        public CoreService(ILoggingService loggingService, INotificationService notificationService, IHealthCheckService healthCheckService, ITradingService tradingService, IWebService webService, IBacktestingService backtestingService)
        {
            this.loggingService = loggingService;
            this.notificationService = notificationService;
            this.healthCheckService = healthCheckService;
            this.tradingService = tradingService;
            this.webService = webService;
            this.backtestingService = backtestingService;

            this.timedTasks = new ConcurrentDictionary<string, HighResolutionTimedTask>();

            // Log unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Set decimal separator to a dot for all cultures
            var cultureInfo = new CultureInfo(CultureInfo.CurrentCulture.Name);
            cultureInfo.NumberFormat.NumberDecimalSeparator = ".";
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            Version = GetType().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
        }

        public void Start()
        {
            loggingService.Info($"Start Core service (Version: {Version})...");
            if (backtestingService.Config.Enabled)
            {
                backtestingService.Start();
                if (backtestingService.Config.Replay)
                {
                    Application.Speed = backtestingService.Config.ReplaySpeed;
                }
            }
            if (Config.HealthCheckInterval > 0 && (!backtestingService.Config.Enabled || !backtestingService.Config.Replay))
            {
                healthCheckService.Start();
            }
            if (tradingService.Config.Enabled)
            {
                tradingService.Start();
            }
            if (notificationService.Config.Enabled)
            {
                // Fire-and-forget async start - notification service startup shouldn't block core service
                _ = notificationService.StartAsync();
            }
            if (webService.Config.Enabled)
            {
                webService.Start();
            }

            // Use Task.Run with Task.Delay instead of ThreadPool with Thread.Sleep
            // to avoid blocking a thread pool thread during the delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                StartAllTasks();
            });

            loggingService.Info("Core service started");
            _ = notificationService.NotifyAsync("IntelliTrader started");
        }

        public void Stop()
        {
            _ = notificationService.NotifyAsync("IntelliTrader stopped");
            loggingService.Info("Stop Core service...");
            if (tradingService.Config.Enabled)
            {
                tradingService.Stop();
            }
            if (notificationService.Config.Enabled)
            {
                notificationService.Stop();
            }
            if (webService.Config.Enabled)
            {
                webService.Stop();
            }
            if (Config.HealthCheckInterval > 0 && (!backtestingService.Config.Enabled || !backtestingService.Config.Replay))
            {
                healthCheckService.Stop();
            }
            if (backtestingService.Config.Enabled)
            {
                backtestingService.Stop();
            }

            StopAllTasks();
            RemoveAllTasks();
            loggingService.Info("Core service stopped");
        }

        public void Restart()
        {
            _ = notificationService.NotifyAsync("IntelliTrader restarting...");
            loggingService.Info("Restart Core service...");
            Task.Run(() => Stop()).Wait(TimeSpan.FromSeconds(20));
            Start();
        }

        public void AddTask(string name, HighResolutionTimedTask task)
        {
            timedTasks[name] = task;
        }

        public void RemoveTask(string name)
        {
            timedTasks.TryRemove(name, out HighResolutionTimedTask task);
        }

        public void RemoveAllTasks()
        {
            timedTasks.Clear();
        }

        public void StartTask(string name)
        {
            if (timedTasks.TryGetValue(name, out HighResolutionTimedTask task))
            {
                task.UnhandledException += OnUnhandledException;
                task.Start();
            }
        }

        public void StopTask(string name)
        {
            if (timedTasks.TryGetValue(name, out HighResolutionTimedTask task))
            {
                task.Stop();
                task.UnhandledException -= OnUnhandledException;
            }
        }

        public void StartAllTasks()
        {
            // Create snapshot of keys to avoid issues if tasks are added during iteration
            var taskNames = timedTasks.Keys.ToList();
            foreach (var taskName in taskNames)
            {
                StartTask(taskName);
            }
        }

        public void StopAllTasks()
        {
            // Create snapshot of keys to avoid issues if tasks are removed during iteration
            var taskNames = timedTasks.Keys.ToList();
            foreach (var taskName in taskNames)
            {
                StopTask(taskName);
            }
        }

        public HighResolutionTimedTask GetTask(string name)
        {
            if (timedTasks.TryGetValue(name, out HighResolutionTimedTask task))
            {
                return task;
            }
            else
            {
                return null;
            }
        }

        public ConcurrentDictionary<string, HighResolutionTimedTask> GetAllTasks()
        {
            return timedTasks;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string message = "Unhandled exception occured";
            if (e.ExceptionObject != null)
            {
                message = $"{message} - {e.ExceptionObject}";
            }
            try
            {
                loggingService.Error(message);
                _ = notificationService.NotifyAsync(message);
            } catch { }
        }
    }
}
