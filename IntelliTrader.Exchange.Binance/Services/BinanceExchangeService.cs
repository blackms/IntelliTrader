using ExchangeSharp;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using IntelliTrader.Exchange.Binance.Config;
using IntelliTrader.Exchange.Binance.Resilience;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Binance
{
    /// <summary>
    /// Binance exchange service with WebSocket streaming and REST fallback support.
    /// </summary>
    internal class BinanceExchangeService : ExchangeService
    {
        /// <summary>
        /// Maximum age of ticker data before forcing a reconnection (seconds).
        /// </summary>
        public const int MaxTickersAgeToReconnectSeconds = 60;

        private readonly ILoggingService _loggingService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly ICoreService _coreService;

        private ExchangeBinanceAPI? _binanceApi;
        private BinanceWebSocketService? _webSocketService;
        private ConcurrentDictionary<string, Ticker> _tickers = new();
        private BinanceTickersMonitorTimedTask? _tickersMonitorTimedTask;
        private bool _tickersChecked;
        private bool _started;

        // Polly resilience pipelines for exchange operations
        private readonly ExchangeResiliencePipelines _resiliencePipelines;

        /// <inheritdoc />
        public override bool IsWebSocketConnected => _webSocketService?.IsConnected ?? false;

        /// <inheritdoc />
        public override bool IsRestFallbackActive => _webSocketService?.IsRestFallbackActive ?? false;

        /// <inheritdoc />
        public override TimeSpan TimeSinceLastTickerUpdate => _webSocketService?.TimeSinceLastUpdate ?? TimeSpan.MaxValue;

        public BinanceExchangeService(
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            ICoreService coreService,
            IConfigProvider configProvider)
            : base(loggingService, healthCheckService, coreService, configProvider)
        {
            _loggingService = loggingService;
            _healthCheckService = healthCheckService;
            _coreService = coreService;

            // Initialize resilience pipelines with separate policies for reads and orders
            // - ReadPipeline: 3 retries, 30s timeout, circuit breaker (for GetTickers, GetAvailableAmounts, GetMyTrades)
            // - OrderPipeline: 1 retry ONLY, 15s timeout, stricter circuit breaker (for PlaceOrder - CRITICAL)
            // - WebSocketPipeline: For connection management
            _resiliencePipelines = new ExchangeResiliencePipelines(loggingService, ResilienceConfig.Default);

            _loggingService.Info("[Resilience] Exchange resilience pipelines initialized");
        }

        public override void Start(bool virtualTrading)
        {
            _loggingService.Info("Start Binance Exchange service...");

            _binanceApi = new ExchangeBinanceAPI();
            _binanceApi.RateLimit = new RateGate(Config.RateLimitOccurences, TimeSpan.FromSeconds(Config.RateLimitTimeframe));

            if (!virtualTrading && !string.IsNullOrWhiteSpace(Config.KeysPath))
            {
                if (File.Exists(Config.KeysPath))
                {
                    _loggingService.Info("Load keys from encrypted file...");
                    _binanceApi.LoadAPIKeys(Config.KeysPath);
                }
                else
                {
                    throw new FileNotFoundException("Keys file not found");
                }
            }

            // Initialize WebSocket service
            _webSocketService = new BinanceWebSocketService(_loggingService, _healthCheckService);
            _webSocketService.TickersUpdated += OnWebSocketTickersUpdated;
            _webSocketService.ConnectionStateChanged += OnConnectionStateChanged;

            // Get initial ticker values via REST API before starting WebSocket
            _loggingService.Info("Get initial ticker values via REST...");
            try
            {
                var initialTickers = _webSocketService.FetchTickersViaRestAsync().GetAwaiter().GetResult();
                _tickers = new ConcurrentDictionary<string, Ticker>(
                    initialTickers.Select(t => new KeyValuePair<string, Ticker>(t.Pair, new Ticker
                    {
                        Pair = t.Pair,
                        AskPrice = t.AskPrice,
                        BidPrice = t.BidPrice,
                        LastPrice = t.LastPrice
                    })));
                _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, $"Initial: {_tickers.Count} tickers");
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"Failed to get initial tickers via REST, falling back to ExchangeSharp: {ex.Message}");
                var initialTickers = _binanceApi.GetTickersAsync().GetAwaiter().GetResult();
                _tickers = new ConcurrentDictionary<string, Ticker>(initialTickers.Select(t => new KeyValuePair<string, Ticker>(t.Key, new Ticker
                {
                    Pair = t.Key,
                    AskPrice = t.Value.Ask,
                    BidPrice = t.Value.Bid,
                    LastPrice = t.Value.Last
                })));
            }

            // Connect WebSocket for real-time updates
            ConnectTickersWebsocket();

            _started = true;
            _loggingService.Info("Binance Exchange service started");
        }

        public override void Stop()
        {
            _loggingService.Info("Stop Binance Exchange service...");

            _started = false;
            DisconnectTickersWebsocket();

            if (_webSocketService != null)
            {
                _webSocketService.TickersUpdated -= OnWebSocketTickersUpdated;
                _webSocketService.ConnectionStateChanged -= OnConnectionStateChanged;
                _webSocketService.Dispose();
                _webSocketService = null;
            }

            _tickers.Clear();
            _healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TickersUpdated);

            _loggingService.Info("Binance Exchange service stopped");
        }

        /// <summary>
        /// Connects to the Binance WebSocket ticker stream.
        /// </summary>
        public void ConnectTickersWebsocket()
        {
            try
            {
                _loggingService.Info("Connect to Binance Exchange tickers via WebSocket...");

                _webSocketService?.ConnectAsync().GetAwaiter().GetResult();

                // Start monitor task to check connection health
                _tickersMonitorTimedTask = new BinanceTickersMonitorTimedTask(_loggingService, this);
                _tickersMonitorTimedTask.RunInterval = MaxTickersAgeToReconnectSeconds / 2;
                _coreService.AddTask(nameof(BinanceTickersMonitorTimedTask), _tickersMonitorTimedTask);

                _loggingService.Info("Connected to Binance Exchange tickers");
            }
            catch (Exception ex)
            {
                _loggingService.Error("Unable to connect to Binance Exchange tickers via WebSocket", ex);
                _loggingService.Info("WebSocket will fall back to REST polling automatically");
            }
        }

        /// <summary>
        /// Disconnects from the Binance WebSocket ticker stream.
        /// </summary>
        public void DisconnectTickersWebsocket()
        {
            try
            {
                _coreService.StopTask(nameof(BinanceTickersMonitorTimedTask));
                _coreService.RemoveTask(nameof(BinanceTickersMonitorTimedTask));

                _loggingService.Info("Disconnect from Binance Exchange tickers...");

                _webSocketService?.DisconnectAsync().GetAwaiter().GetResult();

                _loggingService.Info("Disconnected from Binance Exchange tickers");
            }
            catch (Exception ex)
            {
                _loggingService.Error("Unable to disconnect from Binance Exchange tickers", ex);
            }
        }

        /// <inheritdoc />
        public override async Task ReconnectWebSocketAsync()
        {
            if (_webSocketService != null)
            {
                await _webSocketService.ReconnectAsync();
            }
        }

        /// <summary>
        /// Gets the time elapsed since the last ticker update.
        /// Used by the monitor task to determine if reconnection is needed.
        /// </summary>
        public TimeSpan GetTimeElapsedSinceLastTickersUpdate()
        {
            return _webSocketService?.TimeSinceLastUpdate ?? TimeSpan.MaxValue;
        }

        public override Task<IEnumerable<ITicker>> GetTickers(string market)
        {
            return Task.FromResult(_tickers.Values.Where(t => t.Pair.EndsWith(market)).Select(t => (ITicker)t));
        }

        public override Task<IEnumerable<string>> GetMarketPairs(string market)
        {
            return Task.FromResult(_tickers.Keys.Where(t => t.EndsWith(market)));
        }

        public override async Task<Dictionary<string, decimal>> GetAvailableAmounts()
        {
            if (_binanceApi == null)
            {
                throw new InvalidOperationException("Binance API not initialized");
            }

            // Use ReadPipeline for idempotent read operations
            return await _resiliencePipelines.ReadPipeline.ExecuteAsync(async cancellationToken =>
            {
                var results = await _binanceApi.GetAmountsAvailableToTradeAsync();
                return results;
            });
        }

        public override async Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair)
        {
            if (_binanceApi == null)
            {
                throw new InvalidOperationException("Binance API not initialized");
            }

            // Use ReadPipeline for idempotent read operations
            return await _resiliencePipelines.ReadPipeline.ExecuteAsync(async cancellationToken =>
            {
                var myTrades = new List<OrderDetails>();
                var results = await _binanceApi.GetMyTradesAsync(pair);

                foreach (var result in results)
                {
                    myTrades.Add(new OrderDetails
                    {
                        Side = result.IsBuy ? OrderSide.Buy : OrderSide.Sell,
                        Result = (OrderResult)(int)result.Result,
                        Date = result.OrderDate,
                        OrderId = result.OrderId,
                        Pair = result.MarketSymbol,
                        Message = result.Message,
                        Amount = result.Amount,
                        AmountFilled = result.AmountFilled ?? 0m,
                        Price = result.Price ?? 0m,
                        AveragePrice = result.AveragePrice ?? 0m,
                        Fees = result.Fees ?? 0m,
                        FeesCurrency = result.FeesCurrency
                    });
                }

                return (IEnumerable<IOrderDetails>)myTrades;
            });
        }

        public override Task<decimal> GetLastPrice(string pair)
        {
            if (_tickers.TryGetValue(pair, out Ticker? ticker))
            {
                return Task.FromResult(ticker.LastPrice);
            }
            else
            {
                return Task.FromResult(0m);
            }
        }

        public override async Task<IOrderDetails> PlaceOrder(IOrder order)
        {
            if (_binanceApi == null)
            {
                throw new InvalidOperationException("Binance API not initialized");
            }

            // CRITICAL: Use OrderPipeline for non-idempotent order operations.
            // - Max 1 retry only (to prevent duplicate orders)
            // - Only retries on true connection errors, NOT on HTTP responses
            // - 15s timeout (markets move fast, stale orders are problematic)
            // - Stricter circuit breaker (30% failure ratio vs 50% for reads)
            return await _resiliencePipelines.OrderPipeline.ExecuteAsync(async cancellationToken =>
            {
                var result = await _binanceApi.PlaceOrderAsync(new ExchangeOrderRequest
                {
                    OrderType = (ExchangeSharp.OrderType)(int)order.Type,
                    IsBuy = order.Side == OrderSide.Buy,
                    Amount = order.Amount,
                    Price = order.Price,
                    MarketSymbol = order.Pair
                });

                return (IOrderDetails)new OrderDetails
                {
                    Side = result.IsBuy ? OrderSide.Buy : OrderSide.Sell,
                    Result = (OrderResult)(int)result.Result,
                    Date = result.OrderDate,
                    OrderId = result.OrderId,
                    Pair = result.MarketSymbol,
                    Message = result.Message,
                    Amount = result.Amount,
                    AmountFilled = result.AmountFilled ?? 0m,
                    Price = result.Price ?? 0m,
                    AveragePrice = result.AveragePrice ?? 0m,
                    Fees = result.Fees ?? 0m,
                    FeesCurrency = result.FeesCurrency
                };
            });
        }

        /// <inheritdoc />
        public override bool UpdateCredentials(string keysFilePath)
        {
            if (_binanceApi == null)
            {
                _loggingService.Error("Cannot update credentials: Binance API not initialized");
                return false;
            }

            try
            {
                _binanceApi.LoadAPIKeys(keysFilePath);
                _loggingService.Info("Binance API credentials updated successfully");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to update Binance API credentials", ex);
                return false;
            }
        }

        private void OnWebSocketTickersUpdated(IReadOnlyCollection<ITicker> updatedTickers)
        {
            if (!_started) return;

            if (!_tickersChecked)
            {
                _loggingService.Info("Ticker updates are working via WebSocket, good!");
                _tickersChecked = true;
            }

            var tickersList = new List<ITicker>();

            foreach (var update in updatedTickers)
            {
                var ticker = _tickers.AddOrUpdate(
                    update.Pair,
                    key => new Ticker
                    {
                        Pair = key,
                        AskPrice = update.AskPrice,
                        BidPrice = update.BidPrice,
                        LastPrice = update.LastPrice
                    },
                    (key, existing) =>
                    {
                        existing.AskPrice = update.AskPrice;
                        existing.BidPrice = update.BidPrice;
                        existing.LastPrice = update.LastPrice;
                        return existing;
                    });

                tickersList.Add(ticker);
            }

            // Raise the TickersUpdated event from base class
            OnTickersUpdated(tickersList);
        }

        private void OnConnectionStateChanged(WebSocketConnectionState state)
        {
            switch (state)
            {
                case WebSocketConnectionState.Connected:
                    _loggingService.Info("WebSocket connection established");
                    _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, "WebSocket connected");
                    break;

                case WebSocketConnectionState.Disconnected:
                    _loggingService.Warning("WebSocket connection lost");
                    break;

                case WebSocketConnectionState.Reconnecting:
                    _loggingService.Info("WebSocket reconnecting...");
                    _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, "WebSocket reconnecting");
                    break;

                case WebSocketConnectionState.FallbackToRest:
                    _loggingService.Warning("WebSocket unavailable, using REST fallback");
                    _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, "REST fallback active");
                    break;
            }
        }
    }
}
