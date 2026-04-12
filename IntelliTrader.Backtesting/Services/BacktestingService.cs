using IntelliTrader.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

#pragma warning disable CS0612 // Type or member is obsolete
using System.Threading;

namespace IntelliTrader.Backtesting
{
    // Both ICoreService and ITradingService are injected as Lazy<T> on
    // purpose: CoreService depends on IBacktestingService and
    // TradingService depends on IBacktestingService, so direct injection
    // would create cycles at container build time. Every use of these
    // services happens at Start()/Stop()/Complete()/runtime methods,
    // never inside the constructor body.
    internal class BacktestingService(
        ILoggingService loggingService,
        IHealthCheckService healthCheckService,
        Lazy<ICoreService> coreService,
        ISignalsService signalsService,
        Lazy<ITradingService> tradingService,
        IConfigProvider configProvider) : ConfigurableServiceBase<BacktestingConfig>(configProvider), IBacktestingService
    {
        public const string SNAPSHOT_FILE_EXTENSION = "bin";

        public override string ServiceName => Constants.ServiceNames.BacktestingService;

        protected override ILoggingService LoggingService => loggingService;

        IBacktestingConfig IBacktestingService.Config => Config;

        private readonly object _syncRoot = new object();
        public object SyncRoot => _syncRoot;

        private BacktestingLoadSnapshotsTimedTask backtestingLoadSnapshotsTimedTask;
        private BacktestingSaveSnapshotsTimedTask backtestingSaveSnapshotsTimedTask;

        public void Start()
        {
            loggingService.Info($"Start Backtesting service... (Replay: {Config.Replay})");

            if (Config.Replay)
            {
                backtestingLoadSnapshotsTimedTask = new BacktestingLoadSnapshotsTimedTask(loggingService, healthCheckService, tradingService.Value, this);
                backtestingLoadSnapshotsTimedTask.RunInterval = (float)(Config.SnapshotsInterval / Config.ReplaySpeed * 1000);
                backtestingLoadSnapshotsTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / Config.ReplaySpeed;
                coreService.Value.AddTask(nameof(BacktestingLoadSnapshotsTimedTask), backtestingLoadSnapshotsTimedTask);
            }

            backtestingSaveSnapshotsTimedTask = new BacktestingSaveSnapshotsTimedTask(loggingService, healthCheckService, tradingService.Value, signalsService, this);
            backtestingSaveSnapshotsTimedTask.RunInterval = Config.SnapshotsInterval * 1000;
            backtestingSaveSnapshotsTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / Config.ReplaySpeed;
            coreService.Value.AddTask(nameof(BacktestingSaveSnapshotsTimedTask), backtestingSaveSnapshotsTimedTask);

            if (Config.DeleteLogs)
            {
                loggingService.DeleteAllLogs();
            }

            string virtualAccountPath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Value.Config.VirtualAccountFilePath);
            if (File.Exists(virtualAccountPath) && (Config.DeleteAccountData || !String.IsNullOrWhiteSpace(Config.CopyAccountDataPath)))
            {
                File.Delete(virtualAccountPath);
            }

            if (!String.IsNullOrWhiteSpace(Config.CopyAccountDataPath))
            {
                File.Copy(Path.Combine(Directory.GetCurrentDirectory(), Config.CopyAccountDataPath), virtualAccountPath, true);
            }

            loggingService.Info("Backtesting service started");
        }

        public void Stop()
        {
            loggingService.Info("Stop Backtesting service...");

            if (Config.Replay)
            {
                coreService.Value.StopTask(nameof(BacktestingLoadSnapshotsTimedTask));
                coreService.Value.RemoveTask(nameof(BacktestingLoadSnapshotsTimedTask));
            }

            coreService.Value.StopTask(nameof(BacktestingSaveSnapshotsTimedTask));
            coreService.Value.RemoveTask(nameof(BacktestingSaveSnapshotsTimedTask));

            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.BacktestingSignalsSnapshotTaken);
            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.BacktestingTickersSnapshotTaken);
            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.BacktestingSignalsSnapshotLoaded);
            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.BacktestingTickersSnapshotLoaded);

            loggingService.Info("Backtesting service stopped");
        }

        public void Complete(int skippedSignalSnapshots, int skippedTickerSnapshots)
        {
            loggingService.Info("Backtesting results:");

            double lagAmount = 0;
            foreach (var t in coreService.Value.GetAllTasks().OrderBy(t => t.Key))
            {
                string taskName = t.Key;
                HighResolutionTimedTask task = t.Value;

                double averageWaitTime = Math.Round(task.TotalWaitTime / task.RunTimes, 3);
                if (averageWaitTime > 0) lagAmount += averageWaitTime;
                loggingService.Info($" [+] {taskName} Run times: {task.RunTimes}, average wait time: " + averageWaitTime);
            }

            loggingService.Info($"Lag value: {lagAmount}. Lower the ReplaySpeed if lag value is positive.");
            loggingService.Info($"Skipped signal snapshots: {skippedSignalSnapshots}");
            loggingService.Info($"Skipped ticker snapshots: {skippedTickerSnapshots}");

            tradingService.Value.SuspendTrading(forced: true);
            signalsService.ClearTrailing();
            signalsService.Stop();
        }

        public string GetSnapshotFilePath(string snapshotEntity)
        {
            var date = DateTimeOffset.UtcNow;
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                Config.SnapshotsPath,
                snapshotEntity,
                date.ToString("yyyy-MM-dd"),
                date.ToString("HH"),
                date.ToString("mm-ss-fff")
            ) + "." + SNAPSHOT_FILE_EXTENSION;
        }

        public Dictionary<string, IEnumerable<ISignal>> GetCurrentSignals()
        {
            return backtestingLoadSnapshotsTimedTask.GetCurrentSignals() ?? new Dictionary<string, IEnumerable<ISignal>>();
        }

        public Dictionary<string, ITicker> GetCurrentTickers()
        {
            return backtestingLoadSnapshotsTimedTask.GetCurrentTickers() ?? new Dictionary<string, ITicker>();
        }

        public int GetTotalSnapshots()
        {
            return backtestingLoadSnapshotsTimedTask.GetTotalSnapshots();
        }
    }
}
