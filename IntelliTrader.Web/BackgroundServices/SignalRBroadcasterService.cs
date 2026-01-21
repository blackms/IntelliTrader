using IntelliTrader.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Web.BackgroundServices
{
    /// <summary>
    /// Background service that broadcasts trading updates to SignalR clients at regular intervals.
    /// This replaces the polling-based approach in the JavaScript dashboard with push-based updates.
    /// </summary>
    public class SignalRBroadcasterService : BackgroundService
    {
        private readonly ILogger<SignalRBroadcasterService> _logger;
        private readonly ITradingHubNotifier _hubNotifier;
        private readonly ITradingService _tradingService;
        private readonly ISignalsService _signalsService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly ILoggingService _loggingService;

        // Broadcast intervals - can be made configurable
        private static readonly TimeSpan StatusBroadcastInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PriceBroadcastInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

        public SignalRBroadcasterService(
            ILogger<SignalRBroadcasterService> logger,
            ITradingHubNotifier hubNotifier,
            ITradingService tradingService,
            ISignalsService signalsService,
            IHealthCheckService healthCheckService,
            ILoggingService loggingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hubNotifier = hubNotifier ?? throw new ArgumentNullException(nameof(hubNotifier));
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SignalR Broadcaster Service starting");

            // Wait for services to initialize
            await Task.Delay(StartupDelay, stoppingToken);

            var lastStatusBroadcast = DateTime.MinValue;
            var lastPriceBroadcast = DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // Broadcast status updates (balance, health, trailing)
                    if (now - lastStatusBroadcast >= StatusBroadcastInterval)
                    {
                        await BroadcastStatusAsync();
                        lastStatusBroadcast = now;
                    }

                    // Broadcast price/ticker updates (more frequent)
                    if (now - lastPriceBroadcast >= PriceBroadcastInterval)
                    {
                        await BroadcastTradingPairsAsync();
                        lastPriceBroadcast = now;
                    }

                    // Small delay to prevent tight loop
                    await Task.Delay(500, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Expected during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting SignalR updates");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }

            _logger.LogInformation("SignalR Broadcaster Service stopped");
        }

        private async Task BroadcastStatusAsync()
        {
            try
            {
                var account = _tradingService.Account;
                if (account == null)
                {
                    return;
                }

                var status = new
                {
                    Balance = account.GetBalance(),
                    GlobalRating = _signalsService.GetGlobalRating()?.ToString("0.000") ?? "N/A",
                    TrailingBuys = _tradingService.GetTrailingBuys(),
                    TrailingSells = _tradingService.GetTrailingSells(),
                    TrailingSignals = _signalsService.GetTrailingSignals(),
                    TradingSuspended = _tradingService.IsTradingSuspended,
                    HealthChecks = _healthCheckService.GetHealthChecks()?.Select(h => new
                    {
                        h.Name,
                        h.Message,
                        h.LastUpdated,
                        h.Failed
                    }).ToList(),
                    LogEntries = _loggingService.GetLogEntries()?.TakeLast(10).ToList() ?? new System.Collections.Generic.List<string>(),
                    Timestamp = DateTimeOffset.UtcNow
                };

                await _hubNotifier.BroadcastStatusUpdateAsync(status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast status update");
            }
        }

        private async Task BroadcastTradingPairsAsync()
        {
            try
            {
                var account = _tradingService.Account;
                if (account == null)
                {
                    return;
                }

                var tradingPairs = account.GetTradingPairs()?.ToList();
                if (tradingPairs == null || tradingPairs.Count == 0)
                {
                    return;
                }

                // Broadcast position updates for each trading pair
                foreach (var pair in tradingPairs)
                {
                    try
                    {
                        // Update current price
                        var currentPrice = _tradingService.GetCurrentPrice(pair.Pair);
                        if (currentPrice > 0)
                        {
                            pair.SetCurrentPrice(currentPrice);
                        }

                        await _hubNotifier.BroadcastPositionChangedAsync(pair, "Updated");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to broadcast update for pair {Pair}", pair.Pair);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast trading pairs update");
            }
        }
    }
}
