using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS0612 // Type or member is obsolete

namespace IntelliTrader.Core
{
    internal class CoreService(
        ILoggingService loggingService,
        INotificationService notificationService,
        IHealthCheckService healthCheckService,
        ITradingService tradingService,
        IWebService webService,
        IBacktestingService backtestingService,
        IAlertingService alertingService,
        IApplicationContext applicationContext,
        IConfigProvider configProvider,
        Lazy<ISecretRotationService> secretRotationService) : ConfigurableServiceBase<CoreConfig>(configProvider), ICoreService
    {
        private readonly IApplicationContext _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        public override string ServiceName => Constants.ServiceNames.CoreService;

        protected override ILoggingService LoggingService => loggingService;

        ICoreConfig ICoreService.Config => Config;

        public string Version { get; private set; } = typeof(CoreService).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

        public bool Running { get; private set; }

        private readonly ConcurrentDictionary<string, HighResolutionTimedTask> timedTasks = new ConcurrentDictionary<string, HighResolutionTimedTask>();

        // Static constructor to set up global handlers and culture
        static CoreService()
        {
            // Set decimal separator to a dot for all cultures
            var cultureInfo = new CultureInfo(CultureInfo.CurrentCulture.Name);
            cultureInfo.NumberFormat.NumberDecimalSeparator = ".";
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        }

        public void Start()
        {
            // Register unhandled exception handler
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            loggingService.Info($"Start Core service (Version: {Version})...");
            if (backtestingService.Config.Enabled)
            {
                backtestingService.Start();
                if (backtestingService.Config.Replay)
                {
                    _applicationContext.Speed = backtestingService.Config.ReplaySpeed;
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
            alertingService.Start();

            try
            {
                secretRotationService.Value.Start();
            }
            catch (Exception ex)
            {
                loggingService.Error("Failed to start secret rotation service", ex);
            }

            // Use Task.Run with Task.Delay instead of ThreadPool with Thread.Sleep
            // to avoid blocking a thread pool thread during the delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(Constants.Timeouts.StartupDelayMs);
                StartAllTasks();
            });

            Running = true;
            loggingService.Info("Core service started");
            _ = notificationService.NotifyAsync("IntelliTrader started");
        }

        public void Stop()
        {
            Running = false;
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
            alertingService.Stop();

            try
            {
                secretRotationService.Value.Stop();
            }
            catch (Exception ex)
            {
                loggingService.Error("Failed to stop secret rotation service", ex);
            }

            StopAllTasks();
            RemoveAllTasks();
            loggingService.Info("Core service stopped");
        }

        public void Restart()
        {
            // Synchronous wrapper - calls async implementation with timeout
            RestartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task RestartAsync(CancellationToken cancellationToken = default)
        {
            _ = notificationService.NotifyAsync("IntelliTrader restarting...");
            loggingService.Info("Restart Core service...");

            // Use Task.Run to offload Stop() to thread pool with proper timeout handling
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(Constants.Timeouts.RestartTimeoutSeconds));
                try
                {
                    await Task.Run(() => Stop(), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    loggingService.Info("Stop operation timed out during restart, proceeding with Start...");
                }
            }

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
            }
            catch (Exception logEx)
            {
                // Last resort: write to console if logging/notification fails
                // This ensures we don't lose visibility into the original unhandled exception
                Console.Error.WriteLine($"[CRITICAL] Failed to log unhandled exception. Logging error: {logEx.Message}");
                Console.Error.WriteLine($"[CRITICAL] Original exception: {message}");
            }
        }
    }
}
