using IntelliTrader.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelliTrader.Web.Hubs
{
    /// <summary>
    /// SignalR hub for real-time trading updates.
    /// Clients can subscribe to receive live price updates, trade notifications,
    /// and status changes without polling.
    /// </summary>
    [Authorize]
    public class TradingHub : Hub
    {
        /// <summary>
        /// Group name for all trading updates.
        /// </summary>
        public const string TradingUpdatesGroup = "TradingUpdates";

        /// <summary>
        /// Prefix for pair-specific groups.
        /// </summary>
        public const string PairGroupPrefix = "Pair_";

        /// <summary>
        /// Maximum number of pair subscriptions allowed per connection.
        /// </summary>
        private const int MaxSubscriptionsPerConnection = 50;

        private static readonly Regex ValidPairPattern = new(@"^[A-Z0-9]{2,20}$", RegexOptions.Compiled);

        private static readonly ConcurrentDictionary<string, HashSet<string>> _pairSubscriptions = new();
        private static readonly object _subscriptionLock = new();

        private readonly ILogger<TradingHub> _logger;
        private readonly ITradingService _tradingService;
        private readonly ISignalsService _signalsService;
        private readonly IHealthCheckService _healthCheckService;

        public TradingHub(
            ILogger<TradingHub> logger,
            ITradingService tradingService,
            ISignalsService signalsService,
            IHealthCheckService healthCheckService)
        {
            _logger = logger;
            _tradingService = tradingService;
            _signalsService = signalsService;
            _healthCheckService = healthCheckService;
        }

        /// <summary>
        /// Called when a client connects to the hub.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, TradingUpdatesGroup);

            // Send initial status to the newly connected client
            await SendInitialStatusAsync();

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a client disconnects from the hub.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TradingUpdatesGroup);

            // Clean up pair subscriptions
            CleanupPairSubscriptions(Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Subscribe to updates for a specific trading pair.
        /// </summary>
        /// <param name="pair">The trading pair to subscribe to (e.g., "BTCUSDT").</param>
        public async Task SubscribeToPair(string pair)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                _logger.LogWarning("Client {ConnectionId} attempted to subscribe with null/empty pair", Context.ConnectionId);
                return;
            }

            var normalizedPair = pair.ToUpperInvariant();

            if (!ValidPairPattern.IsMatch(normalizedPair))
            {
                _logger.LogWarning("Client {ConnectionId} attempted to subscribe with invalid pair format: {Pair}", Context.ConnectionId, normalizedPair);
                return;
            }

            // Check subscription limit per connection
            lock (_subscriptionLock)
            {
                int currentCount = _pairSubscriptions.Count(kvp => kvp.Value.Contains(Context.ConnectionId));
                if (currentCount >= MaxSubscriptionsPerConnection)
                {
                    _logger.LogWarning("Client {ConnectionId} exceeded max subscriptions ({Max})", Context.ConnectionId, MaxSubscriptionsPerConnection);
                    return;
                }
            }

            var groupName = $"{PairGroupPrefix}{normalizedPair}";

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            // Track subscription
            lock (_subscriptionLock)
            {
                if (!_pairSubscriptions.TryGetValue(normalizedPair, out var subscribers))
                {
                    subscribers = new HashSet<string>();
                    _pairSubscriptions[normalizedPair] = subscribers;
                }
                subscribers.Add(Context.ConnectionId);
            }

            _logger.LogDebug("Client {ConnectionId} subscribed to pair {Pair}", Context.ConnectionId, normalizedPair);

            // Send current price for this pair
            await SendPairPriceAsync(normalizedPair);
        }

        /// <summary>
        /// Unsubscribe from updates for a specific trading pair.
        /// </summary>
        /// <param name="pair">The trading pair to unsubscribe from.</param>
        public async Task UnsubscribeFromPair(string pair)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                return;
            }

            var normalizedPair = pair.ToUpperInvariant();
            var groupName = $"{PairGroupPrefix}{normalizedPair}";

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

            // Remove from tracked subscriptions
            lock (_subscriptionLock)
            {
                if (_pairSubscriptions.TryGetValue(normalizedPair, out var subscribers))
                {
                    subscribers.Remove(Context.ConnectionId);
                    if (subscribers.Count == 0)
                    {
                        _pairSubscriptions.TryRemove(normalizedPair, out _);
                    }
                }
            }

            _logger.LogDebug("Client {ConnectionId} unsubscribed from pair {Pair}", Context.ConnectionId, normalizedPair);
        }

        /// <summary>
        /// Request immediate status update.
        /// </summary>
        public async Task RequestStatus()
        {
            await SendInitialStatusAsync();
        }

        /// <summary>
        /// Request current trading pairs data.
        /// </summary>
        public async Task RequestTradingPairs()
        {
            try
            {
                var tradingPairs = _tradingService.Account?.GetTradingPairs()?.ToList();
                if (tradingPairs != null)
                {
                    await Clients.Caller.SendAsync("TradingPairsUpdate", tradingPairs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trading pairs to client {ConnectionId}", Context.ConnectionId);
            }
        }

        /// <summary>
        /// Request current health status.
        /// </summary>
        public async Task RequestHealthStatus()
        {
            try
            {
                var healthChecks = _healthCheckService.GetHealthChecks();
                var status = new
                {
                    HealthChecks = healthChecks,
                    TradingSuspended = _tradingService.IsTradingSuspended,
                    Timestamp = DateTimeOffset.UtcNow
                };
                await Clients.Caller.SendAsync("HealthStatus", status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send health status to client {ConnectionId}", Context.ConnectionId);
            }
        }

        private async Task SendInitialStatusAsync()
        {
            try
            {
                var account = _tradingService.Account;
                var status = new
                {
                    Balance = account?.GetBalance() ?? 0,
                    GlobalRating = _signalsService.GetGlobalRating()?.ToString("0.000") ?? "N/A",
                    TrailingBuys = _tradingService.GetTrailingBuys(),
                    TrailingSells = _tradingService.GetTrailingSells(),
                    TrailingSignals = _signalsService.GetTrailingSignals(),
                    TradingSuspended = _tradingService.IsTradingSuspended,
                    HealthChecks = _healthCheckService.GetHealthChecks(),
                    Timestamp = DateTimeOffset.UtcNow
                };

                await Clients.Caller.SendAsync("StatusUpdate", status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send initial status to client {ConnectionId}", Context.ConnectionId);
            }
        }

        private async Task SendPairPriceAsync(string pair)
        {
            try
            {
                var currentPrice = await _tradingService.GetCurrentPriceAsync(pair).ConfigureAwait(false);
                if (currentPrice > 0)
                {
                    await Clients.Caller.SendAsync("PriceUpdate", new
                    {
                        Pair = pair,
                        Price = currentPrice,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send initial price for pair {Pair}", pair);
            }
        }

        private void CleanupPairSubscriptions(string connectionId)
        {
            lock (_subscriptionLock)
            {
                var emptyPairs = new List<string>();

                foreach (var kvp in _pairSubscriptions)
                {
                    kvp.Value.Remove(connectionId);
                    if (kvp.Value.Count == 0)
                    {
                        emptyPairs.Add(kvp.Key);
                    }
                }

                foreach (var pair in emptyPairs)
                {
                    _pairSubscriptions.TryRemove(pair, out _);
                }
            }
        }
    }
}
