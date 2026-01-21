using IntelliTrader.Core;
using IntelliTrader.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntelliTrader.Web.Services
{
    /// <summary>
    /// Implementation of ITradingHubNotifier that broadcasts real-time updates to SignalR clients.
    /// </summary>
    public class TradingHubNotifier : ITradingHubNotifier
    {
        private readonly IHubContext<TradingHub> _hubContext;
        private readonly ILogger<TradingHubNotifier> _logger;

        public TradingHubNotifier(
            IHubContext<TradingHub> hubContext,
            ILogger<TradingHubNotifier> logger)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task BroadcastPriceUpdateAsync(string pair, decimal price)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                return;
            }

            try
            {
                var normalizedPair = pair.ToUpperInvariant();
                var payload = new
                {
                    Pair = normalizedPair,
                    Price = price,
                    Timestamp = DateTimeOffset.UtcNow
                };

                // Send to pair-specific subscribers
                await _hubContext.Clients
                    .Group($"{TradingHub.PairGroupPrefix}{normalizedPair}")
                    .SendAsync("PriceUpdate", payload);

                _logger.LogTrace("Broadcasted price update for {Pair}: {Price}", normalizedPair, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast price update for {Pair}", pair);
            }
        }

        /// <inheritdoc />
        public async Task BroadcastTickerUpdateAsync(ITicker ticker)
        {
            if (ticker == null)
            {
                return;
            }

            try
            {
                var normalizedPair = ticker.Pair.ToUpperInvariant();
                var payload = new
                {
                    Pair = normalizedPair,
                    ticker.BidPrice,
                    ticker.AskPrice,
                    ticker.LastPrice,
                    Timestamp = DateTimeOffset.UtcNow
                };

                // Send to pair-specific subscribers
                await _hubContext.Clients
                    .Group($"{TradingHub.PairGroupPrefix}{normalizedPair}")
                    .SendAsync("TickerUpdate", payload);

                _logger.LogTrace("Broadcasted ticker update for {Pair}", normalizedPair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast ticker update for {Pair}", ticker.Pair);
            }
        }

        /// <inheritdoc />
        public async Task BroadcastTradeExecutedAsync(IOrderDetails orderDetails)
        {
            if (orderDetails == null)
            {
                return;
            }

            try
            {
                var payload = new
                {
                    orderDetails.OrderId,
                    orderDetails.Pair,
                    Side = orderDetails.Side.ToString(),
                    Result = orderDetails.Result.ToString(),
                    orderDetails.Amount,
                    orderDetails.AmountFilled,
                    orderDetails.Price,
                    orderDetails.AveragePrice,
                    orderDetails.AverageCost,
                    orderDetails.Fees,
                    orderDetails.FeesCurrency,
                    orderDetails.Date,
                    Timestamp = DateTimeOffset.UtcNow
                };

                // Broadcast to all subscribers
                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("TradeExecuted", payload);

                // Also send to pair-specific subscribers
                var normalizedPair = orderDetails.Pair.ToUpperInvariant();
                await _hubContext.Clients
                    .Group($"{TradingHub.PairGroupPrefix}{normalizedPair}")
                    .SendAsync("TradeExecuted", payload);

                _logger.LogDebug("Broadcasted trade executed: {Side} {Pair} @ {Price}",
                    orderDetails.Side, orderDetails.Pair, orderDetails.AveragePrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast trade executed for {Pair}", orderDetails.Pair);
            }
        }

        /// <inheritdoc />
        public async Task BroadcastPositionChangedAsync(ITradingPair tradingPair, string changeType)
        {
            if (tradingPair == null)
            {
                return;
            }

            try
            {
                var payload = new
                {
                    tradingPair.Pair,
                    tradingPair.FormattedName,
                    tradingPair.DCALevel,
                    tradingPair.TotalAmount,
                    tradingPair.AveragePricePaid,
                    tradingPair.AverageCostPaid,
                    tradingPair.CurrentCost,
                    tradingPair.CurrentPrice,
                    tradingPair.CurrentMargin,
                    tradingPair.CurrentAge,
                    ChangeType = changeType,
                    Timestamp = DateTimeOffset.UtcNow
                };

                // Broadcast to all subscribers
                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("PositionChanged", payload);

                // Also send to pair-specific subscribers
                var normalizedPair = tradingPair.Pair.ToUpperInvariant();
                await _hubContext.Clients
                    .Group($"{TradingHub.PairGroupPrefix}{normalizedPair}")
                    .SendAsync("PositionChanged", payload);

                _logger.LogDebug("Broadcasted position change: {ChangeType} {Pair}", changeType, tradingPair.Pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast position change for {Pair}", tradingPair.Pair);
            }
        }

        /// <inheritdoc />
        public async Task BroadcastHealthStatusAsync(IEnumerable<IHealthCheck> healthChecks, bool tradingSuspended)
        {
            try
            {
                var payload = new
                {
                    HealthChecks = healthChecks?.Select(h => new
                    {
                        h.Name,
                        h.Message,
                        h.LastUpdated,
                        h.Failed
                    }).ToList(),
                    TradingSuspended = tradingSuspended,
                    Timestamp = DateTimeOffset.UtcNow
                };

                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("HealthStatus", payload);

                _logger.LogTrace("Broadcasted health status: TradingSuspended={TradingSuspended}", tradingSuspended);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast health status");
            }
        }

        /// <inheritdoc />
        public async Task BroadcastBalanceUpdateAsync(decimal balance)
        {
            try
            {
                var payload = new
                {
                    Balance = balance,
                    Timestamp = DateTimeOffset.UtcNow
                };

                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("BalanceUpdate", payload);

                _logger.LogTrace("Broadcasted balance update: {Balance}", balance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast balance update");
            }
        }

        /// <inheritdoc />
        public async Task BroadcastTrailingStatusAsync(
            List<string> trailingBuys,
            List<string> trailingSells,
            List<string> trailingSignals)
        {
            try
            {
                var payload = new
                {
                    TrailingBuys = trailingBuys ?? new List<string>(),
                    TrailingSells = trailingSells ?? new List<string>(),
                    TrailingSignals = trailingSignals ?? new List<string>(),
                    Timestamp = DateTimeOffset.UtcNow
                };

                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("TrailingStatus", payload);

                _logger.LogTrace("Broadcasted trailing status: Buys={Buys}, Sells={Sells}, Signals={Signals}",
                    trailingBuys?.Count ?? 0, trailingSells?.Count ?? 0, trailingSignals?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast trailing status");
            }
        }

        /// <inheritdoc />
        public async Task BroadcastStatusUpdateAsync(object status)
        {
            if (status == null)
            {
                return;
            }

            try
            {
                await _hubContext.Clients
                    .Group(TradingHub.TradingUpdatesGroup)
                    .SendAsync("StatusUpdate", status);

                _logger.LogTrace("Broadcasted full status update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast status update");
            }
        }
    }
}
