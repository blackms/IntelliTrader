using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Trading
{
    internal class ExchangeAccount : TradingAccountBase
    {
        public ExchangeAccount(ILoggingService loggingService, INotificationService notificationService, IHealthCheckService healthCheckService, ISignalsService signalsService, ITradingService tradingService)
            : base(loggingService, notificationService, healthCheckService, signalsService, tradingService)
        {

        }

        public override void Refresh()
        {
            loggingService.Info("Refresh account...");

            decimal newBalance = 0;
            Dictionary<string, decimal> availableAmounts = new Dictionary<string, decimal>();
            Dictionary<string, IEnumerable<IOrderDetails>> availableTrades = new Dictionary<string, IEnumerable<IOrderDetails>>();
            DateTimeOffset refreshStart = DateTimeOffset.Now;

            // Preload account data without locking the account
            try
            {
                loggingService.Info("Load account data...");

                foreach (var kvp in tradingService.GetAvailableAmounts())
                {
                    string currency = kvp.Key;
                    string pair = currency + tradingService.Config.Market;
                    decimal amount = kvp.Value;
                    decimal price = tradingService.GetCurrentPrice(pair);
                    decimal cost = amount * price;

                    if (currency == tradingService.Config.Market)
                    {
                        newBalance = amount;
                    }
                    else if (cost > tradingService.Config.MinCost && !tradingService.Config.ExcludedPairs.Contains(pair))
                    {
                        try
                        {
                            IEnumerable<IOrderDetails> trades = tradingService.GetMyTrades(pair);
                            availableTrades.Add(pair, trades);
                            availableAmounts.Add(pair, amount);
                        }
                        catch (Exception ex) when (ex.Message != null && ex.Message.Contains("Invalid symbol"))
                        {
                            loggingService.Info($"Skip invalid pair: {pair}");
                        }
                    }
                }

                loggingService.Info("Account data loaded");
            }
            catch (Exception ex) when (!isInitialRefresh)
            {
                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, ex.Message, true);
                loggingService.Error("Unable to load account data", ex);
                _ = notificationService.NotifyAsync("Unable to load account data");
                return;
            }

            // Lock the account and reapply all trades
            try
            {
                lock (SyncRoot)
                {
                    ConcurrentDictionary<string, TradingPair> tradingPairsBackup = null;
                    if (isInitialRefresh)
                    {
                        TradingAccountData data = LoadBackupData();
                        tradingPairsBackup = data?.TradingPairs ?? new ConcurrentDictionary<string, TradingPair>();
                    }
                    else
                    {
                        tradingPairsBackup = tradingPairs;
                    }
                    tradingPairs = new ConcurrentDictionary<string, TradingPair>();

                    foreach (var kvp in availableTrades)
                    {
                        string pair = kvp.Key;
                        decimal amount = availableAmounts[pair];
                        IEnumerable<IOrderDetails> trades = kvp.Value;

                        foreach (var trade in trades)
                        {
                            if (trade.Date >= tradingService.Config.AccountInitialBalanceDate)
                            {
                                if (trade.Side == OrderSide.Buy)
                                {
                                    AddBuyOrder(trade);
                                }
                                else
                                {
                                    ITradeResult tradeResult = AddSellOrder(trade);
                                }

                                if (isInitialRefresh)
                                {
                                    tradingService.LogOrder(trade);
                                }
                            }
                        }

                        if (tradingPairs.TryGetValue(pair, out TradingPair tradingPair) && tradingPair.TotalAmount != amount)
                        {
                            loggingService.Info($"Fix amount for {pair}: {tradingPair.TotalAmount:0.########} => {amount:0.########}");
                            tradingPair.TotalAmount = amount;
                        }
                    }

                    foreach (var pair in tradingPairs.Keys.ToList())
                    {
                        if (tradingPairs[pair].AverageCostPaid <= tradingService.Config.MinCost)
                        {
                            loggingService.Info($"Skip low value pair: {pair}");
                            tradingPairs.TryRemove(pair, out TradingPair p);
                        }
                        else
                        {
                            if (tradingPairsBackup.TryGetValue(pair, out TradingPair backup))
                            {
                                tradingPairs[pair].Metadata = backup.Metadata ?? new OrderMetadata();
                            }
                        }
                    }

                    balance = newBalance;

                    // Add trades that were completed during account refresh
                    foreach (var order in tradingService.OrderHistory)
                    {
                        if (order.Date > refreshStart)
                        {
                            if (tradingPairs.TryGetValue(order.Pair, out TradingPair tradingPair))
                            {
                                if (!tradingPair.OrderIds.Contains(order.OrderId))
                                {
                                    loggingService.Info($"Add missing order for {order.Pair} ({order.OrderId})");
                                    AddOrder(order);
                                }
                            }
                            else
                            {
                                loggingService.Info($"Add missing order for {order.Pair} ({order.OrderId})");
                                AddOrder(order);
                            }
                        }
                    }

                    if (isInitialRefresh)
                    {
                        isInitialRefresh = false;
                        Save();
                    }

                    loggingService.Info($"Account refreshed. Balance: {balance}, Trading pairs: {tradingPairs.Count}");
                    healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, $"Balance: {balance}, Trading pairs: {tradingPairs.Count}");
                }
            }
            catch (Exception ex)
            {
                tradingPairs.Clear();
                tradingService.SuspendTrading();
                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, ex.Message, true);
                loggingService.Error("Unable to refresh account", ex);
                _ = notificationService.NotifyAsync("Unable to refresh account");
            }
        }

        public override void Save()
        {
            lock (SyncRoot)
            {
                try
                {
                    string accountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.AccountFilePath);

                    var data = new TradingAccountData
                    {
                        Balance = balance,
                        TradingPairs = tradingPairs,
                    };

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string accountJson = JsonSerializer.Serialize(data, options);
                    var accountFile = new FileInfo(accountFilePath);
                    accountFile.Directory?.Create();
                    File.WriteAllText(accountFile.FullName, accountJson);
                }
                catch (Exception ex)
                {
                    loggingService.Error("Unable to save account backup data", ex);
                }
            }
        }

        public override async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            loggingService.Info("Refresh account (async)...");

            decimal newBalance = 0;
            Dictionary<string, decimal> availableAmounts = new Dictionary<string, decimal>();
            Dictionary<string, IEnumerable<IOrderDetails>> availableTrades = new Dictionary<string, IEnumerable<IOrderDetails>>();
            DateTimeOffset refreshStart = DateTimeOffset.Now;

            // Preload account data without locking the account using async methods
            try
            {
                loggingService.Info("Load account data (async)...");

                var amounts = await tradingService.GetAvailableAmountsAsync().ConfigureAwait(false);
                foreach (var kvp in amounts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string currency = kvp.Key;
                    string pair = currency + tradingService.Config.Market;
                    decimal amount = kvp.Value;
                    decimal price = await tradingService.GetCurrentPriceAsync(pair).ConfigureAwait(false);
                    decimal cost = amount * price;

                    if (currency == tradingService.Config.Market)
                    {
                        newBalance = amount;
                    }
                    else if (cost > tradingService.Config.MinCost && !tradingService.Config.ExcludedPairs.Contains(pair))
                    {
                        try
                        {
                            IEnumerable<IOrderDetails> trades = await tradingService.GetMyTradesAsync(pair).ConfigureAwait(false);
                            availableTrades.Add(pair, trades);
                            availableAmounts.Add(pair, amount);
                        }
                        catch (Exception ex) when (ex.Message != null && ex.Message.Contains("Invalid symbol"))
                        {
                            loggingService.Info($"Skip invalid pair: {pair}");
                        }
                    }
                }

                loggingService.Info("Account data loaded (async)");
            }
            catch (OperationCanceledException)
            {
                loggingService.Info("Account refresh cancelled");
                throw;
            }
            catch (Exception ex) when (!isInitialRefresh)
            {
                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, ex.Message, true);
                loggingService.Error("Unable to load account data", ex);
                _ = notificationService.NotifyAsync("Unable to load account data");
                return;
            }

            // Lock the account and reapply all trades (same logic as sync version)
            try
            {
                lock (SyncRoot)
                {
                    ConcurrentDictionary<string, TradingPair> tradingPairsBackup = null;
                    if (isInitialRefresh)
                    {
                        TradingAccountData data = LoadBackupData();
                        tradingPairsBackup = data?.TradingPairs ?? new ConcurrentDictionary<string, TradingPair>();
                    }
                    else
                    {
                        tradingPairsBackup = tradingPairs;
                    }
                    tradingPairs = new ConcurrentDictionary<string, TradingPair>();

                    foreach (var kvp in availableTrades)
                    {
                        string pair = kvp.Key;
                        decimal amount = availableAmounts[pair];
                        IEnumerable<IOrderDetails> trades = kvp.Value;

                        foreach (var trade in trades)
                        {
                            if (trade.Date >= tradingService.Config.AccountInitialBalanceDate)
                            {
                                if (trade.Side == OrderSide.Buy)
                                {
                                    AddBuyOrder(trade);
                                }
                                else
                                {
                                    ITradeResult tradeResult = AddSellOrder(trade);
                                }

                                if (isInitialRefresh)
                                {
                                    tradingService.LogOrder(trade);
                                }
                            }
                        }

                        if (tradingPairs.TryGetValue(pair, out TradingPair tradingPair) && tradingPair.TotalAmount != amount)
                        {
                            loggingService.Info($"Fix amount for {pair}: {tradingPair.TotalAmount:0.########} => {amount:0.########}");
                            tradingPair.TotalAmount = amount;
                        }
                    }

                    foreach (var pair in tradingPairs.Keys.ToList())
                    {
                        if (tradingPairs[pair].AverageCostPaid <= tradingService.Config.MinCost)
                        {
                            loggingService.Info($"Skip low value pair: {pair}");
                            tradingPairs.TryRemove(pair, out TradingPair p);
                        }
                        else
                        {
                            if (tradingPairsBackup.TryGetValue(pair, out TradingPair backup))
                            {
                                tradingPairs[pair].Metadata = backup.Metadata ?? new OrderMetadata();
                            }
                        }
                    }

                    balance = newBalance;

                    // Add trades that were completed during account refresh
                    foreach (var order in tradingService.OrderHistory)
                    {
                        if (order.Date > refreshStart)
                        {
                            if (tradingPairs.TryGetValue(order.Pair, out TradingPair tradingPair))
                            {
                                if (!tradingPair.OrderIds.Contains(order.OrderId))
                                {
                                    loggingService.Info($"Add missing order for {order.Pair} ({order.OrderId})");
                                    AddOrder(order);
                                }
                            }
                            else
                            {
                                loggingService.Info($"Add missing order for {order.Pair} ({order.OrderId})");
                                AddOrder(order);
                            }
                        }
                    }

                    if (isInitialRefresh)
                    {
                        isInitialRefresh = false;
                        Save();
                    }

                    loggingService.Info($"Account refreshed (async). Balance: {balance}, Trading pairs: {tradingPairs.Count}");
                    healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, $"Balance: {balance}, Trading pairs: {tradingPairs.Count}");
                }
            }
            catch (Exception ex)
            {
                tradingPairs.Clear();
                tradingService.SuspendTrading();
                healthCheckService.UpdateHealthCheck(Constants.HealthChecks.AccountRefreshed, ex.Message, true);
                loggingService.Error("Unable to refresh account", ex);
                _ = notificationService.NotifyAsync("Unable to refresh account");
            }
        }

        /// <summary>
        /// Asynchronously saves account backup data to file.
        /// </summary>
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            TradingAccountData data;
            string accountFilePath;

            lock (SyncRoot)
            {
                accountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.AccountFilePath);

                data = new TradingAccountData
                {
                    Balance = balance,
                    TradingPairs = tradingPairs,
                };
            }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string accountJson = JsonSerializer.Serialize(data, options);
                var accountFile = new FileInfo(accountFilePath);
                accountFile.Directory?.Create();

#if NETCOREAPP2_1
                // .NET Core 2.1 doesn't have WriteAllTextAsync
                await Task.Run(() => File.WriteAllText(accountFile.FullName, accountJson), cancellationToken).ConfigureAwait(false);
#else
                await File.WriteAllTextAsync(accountFile.FullName, accountJson, cancellationToken).ConfigureAwait(false);
#endif
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to save account backup data (async)", ex);
            }
        }

        /// <summary>
        /// Asynchronously loads backup data from file.
        /// </summary>
        private async Task<TradingAccountData> LoadBackupDataAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                string accountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.AccountFilePath);

                if (File.Exists(accountFilePath))
                {
#if NETCOREAPP2_1
                    // .NET Core 2.1 doesn't have ReadAllTextAsync
                    string accountJson = await Task.Run(() => File.ReadAllText(accountFilePath), cancellationToken).ConfigureAwait(false);
#else
                    string accountJson = await File.ReadAllTextAsync(accountFilePath, cancellationToken).ConfigureAwait(false);
#endif
                    return JsonSerializer.Deserialize<TradingAccountData>(accountJson);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to load account backup data (async)", ex);
                return null;
            }
        }

        private TradingAccountData LoadBackupData()
        {
            lock (SyncRoot)
            {
                try
                {
                    string accountFilePath = Path.Combine(Directory.GetCurrentDirectory(), tradingService.Config.AccountFilePath);

                    if (File.Exists(accountFilePath))
                    {
                        string accountJson = File.ReadAllText(accountFilePath);
                        return JsonSerializer.Deserialize<TradingAccountData>(accountJson);
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    loggingService.Error("Unable to load account backup data", ex);
                    return null;
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
