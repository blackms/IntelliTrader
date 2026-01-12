using IntelliTrader.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IntelliTrader.Backtesting
{
    internal class BacktestingService : ConfigrableServiceBase<BacktestingConfig>, IBacktestingService
    {
        public const string SNAPSHOT_FILE_EXTENSION = "bin";

        public override string ServiceName => Constants.ServiceNames.BacktestingService;

        IBacktestingConfig IBacktestingService.Config => Config;

        public object SyncRoot { get; private set; } = new object();

        private readonly ILoggingService loggingService;
        private readonly IHealthCheckService healthCheckService;
        private readonly ICoreService coreService;
        private readonly ISignalsService signalsService;
        private readonly ITradingService tradingService;
        private BacktestingLoadSnapshotsTimedTask backtestingLoadSnapshotsTimedTask;
        private BacktestingSaveSnapshotsTimedTask backtestingSaveSnapshotsTimedTask;

        public BacktestingService(
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            ICoreService coreService,
            ISignalsService signalsService,
            ITradingService tradingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            this.coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            this.signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));
            this.tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        }

        public void Start()
        {
            loggingService.Info($"Start Backtesting service... (Replay: {Config.Replay})");

            if (Config.Replay)
            {
                backtestingLoadSnapshotsTimedTask = new BacktestingLoadSnapshotsTimedTask(loggingService, healthCheckService, tradingService, this);
                backtestingLoadSnapshotsTimedTask.RunInterval = (float)(Config.SnapshotsInterval / Config.ReplaySpeed * 1000);
                backtestingLoadSnapshotsTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / Config.ReplaySpeed;
                coreService.AddTask(nameof(BacktestingLoadSnapshotsTimedTask), backtestingLoadSnapshotsTimedTask);
            }

            backtestingSaveSnapshotsTimedTask = new BacktestingSaveSnapshotsTimedTask(loggingService, healthCheckService, tradingService, signalsService, this);
            backtestingSaveSnapshotsTimedTask.RunInterval = Config.SnapshotsInterval * 1000;
            backtestingSaveSnapshotsTimedTask.StartDelay = Constants.TimedTasks.StandardDelay / Config.ReplaySpeed;
            coreService.AddTask(nameof(BacktestingSaveSnapshotsTimedTask), backtestingSaveSnapshotsTimedTask);

            if (Config.DeleteLogs)
            {
                loggingService.DeleteAllLogs();
            }

            string virtualAccountPath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.VirtualAccountFilePath);
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
                coreService.StopTask(nameof(BacktestingLoadSnapshotsTimedTask));
                coreService.RemoveTask(nameof(BacktestingLoadSnapshotsTimedTask));
            }

            coreService.StopTask(nameof(BacktestingSaveSnapshotsTimedTask));
            coreService.RemoveTask(nameof(BacktestingSaveSnapshotsTimedTask));

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
            foreach (var t in coreService.GetAllTasks().OrderBy(t => t.Key))
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

            tradingService.SuspendTrading(forced: true);
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
