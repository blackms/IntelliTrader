using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelliTrader.Core;

namespace IntelliTrader.Trading
{
    internal class VirtualAccount : TradingAccountBase
    {
        public VirtualAccount(ILoggingService loggingService, INotificationService notificationService, IHealthCheckService healthCheckService, ISignalsService signalsService, ITradingService tradingService)
            : base(loggingService, notificationService, healthCheckService, signalsService, tradingService)
        {

        }

        public override void Refresh()
        {
            lock (SyncRoot)
            {
                // Only done once, since all the data is always up to date
                if (isInitialRefresh)
                {
                    Load();
                    isInitialRefresh = false;
                }

                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed);
            }
        }

        public override void Save()
        {
            lock (SyncRoot)
            {
                try
                {
                    string virtualAccountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.VirtualAccountFilePath);

                    var data = new TradingAccountData
                    {
                        Balance = balance,
                        TradingPairs = tradingPairs
                    };

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string virtualAccountJson = JsonSerializer.Serialize(data, options);
                    var virtualAccountFile = new FileInfo(virtualAccountFilePath);
                    virtualAccountFile.Directory?.Create();

                    // Atomic write: write to temp file first, then rename
                    var tempPath = virtualAccountFile.FullName + ".tmp";
                    File.WriteAllText(tempPath, virtualAccountJson);
                    File.Move(tempPath, virtualAccountFile.FullName, overwrite: true);
                }
                catch (Exception ex)
                {
                    loggingService.Error("Unable to save virtual account data", ex);
                }
            }
        }

        public void Load()
        {
            lock (SyncRoot)
            {
                var virtualAccountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.VirtualAccountFilePath);

                if (File.Exists(virtualAccountFilePath))
                {
                    string virtualAccountJson = File.ReadAllText(virtualAccountFilePath);
                    var virtualAccountData = JsonSerializer.Deserialize<TradingAccountData>(virtualAccountJson);

                    if (virtualAccountData != null)
                    {
                        balance = virtualAccountData.Balance;
                        tradingPairs = virtualAccountData.TradingPairs ?? new ConcurrentDictionary<string, TradingPair>();
                    }
                }
                else
                {
                    balance = tradingService.Config.VirtualAccountInitialBalance;
                    tradingPairs = new ConcurrentDictionary<string, TradingPair>();
                }
            }
        }

        public override Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            // Virtual account refresh is synchronous (local file only)
            // but we wrap it in a Task for interface compliance
            lock (SyncRoot)
            {
                // Only done once, since all the data is always up to date
                if (isInitialRefresh)
                {
                    Load();
                    isInitialRefresh = false;
                }

                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Asynchronously saves virtual account data to file.
        /// </summary>
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            TradingAccountData data;
            string virtualAccountFilePath;

            lock (SyncRoot)
            {
                virtualAccountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.VirtualAccountFilePath);

                data = new TradingAccountData
                {
                    Balance = balance,
                    TradingPairs = tradingPairs
                };
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string virtualAccountJson = JsonSerializer.Serialize(data, options);
            var virtualAccountFile = new FileInfo(virtualAccountFilePath);
            virtualAccountFile.Directory?.Create();

            // Atomic write: write to temp file first, then rename
            var tempPath = virtualAccountFile.FullName + ".tmp";
#if NETCOREAPP2_1
            // .NET Core 2.1 doesn't have WriteAllTextAsync
            await Task.Run(() =>
            {
                File.WriteAllText(tempPath, virtualAccountJson);
                File.Move(tempPath, virtualAccountFile.FullName, overwrite: true);
            }, cancellationToken).ConfigureAwait(false);
#else
            await File.WriteAllTextAsync(tempPath, virtualAccountJson, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, virtualAccountFile.FullName, overwrite: true);
#endif
        }

        /// <summary>
        /// Asynchronously loads virtual account data from file.
        /// </summary>
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            var virtualAccountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.VirtualAccountFilePath);

            if (File.Exists(virtualAccountFilePath))
            {
#if NETCOREAPP2_1
                // .NET Core 2.1 doesn't have ReadAllTextAsync
                string virtualAccountJson = await Task.Run(() => File.ReadAllText(virtualAccountFilePath), cancellationToken).ConfigureAwait(false);
#else
                string virtualAccountJson = await File.ReadAllTextAsync(virtualAccountFilePath, cancellationToken).ConfigureAwait(false);
#endif
                var virtualAccountData = JsonSerializer.Deserialize<TradingAccountData>(virtualAccountJson);

                lock (SyncRoot)
                {
                    if (virtualAccountData != null)
                    {
                        balance = virtualAccountData.Balance;
                        tradingPairs = virtualAccountData.TradingPairs ?? new ConcurrentDictionary<string, TradingPair>();
                    }
                }
            }
            else
            {
                lock (SyncRoot)
                {
                    balance = tradingService.Config.VirtualAccountInitialBalance;
                    tradingPairs = new ConcurrentDictionary<string, TradingPair>();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                healthCheckService.RemoveHealthCheck(Constants.HealthChecks.AccountRefreshed);
            }
        }
    }
}
