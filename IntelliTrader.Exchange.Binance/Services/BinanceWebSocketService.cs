using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using IntelliTrader.Exchange.Binance.Models;

namespace IntelliTrader.Exchange.Binance
{
    /// <summary>
    /// Binance WebSocket service for real-time ticker streaming.
    /// Implements connection lifecycle management, automatic reconnection, ping/pong handling,
    /// and REST API fallback when WebSocket is unavailable.
    /// </summary>
    internal class BinanceWebSocketService : IBinanceWebSocketService
    {
        // Use constants from Core for configuration values
        private static readonly string WebSocketBaseUrl = Constants.WebSocket.BinanceStreamUrl;
        private static readonly string CombinedStreamUrl = Constants.WebSocket.BinanceCombinedStreamUrl;
        private const string RestApiBaseUrl = "https://api.binance.com/api/v3";
        private static readonly int PingIntervalSeconds = Constants.WebSocket.PingIntervalSeconds;
        private static readonly int ReconnectDelaySeconds = Constants.WebSocket.ReconnectDelaySeconds;
        private static readonly int MaxReconnectAttempts = Constants.WebSocket.MaxReconnectAttempts;
        private static readonly int ReceiveBufferSize = Constants.WebSocket.ReceiveBufferSize;
        private static readonly int MaxMessageSize = Constants.WebSocket.MaxMessageSize;

        private readonly ILoggingService _loggingService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, Ticker> _tickers;
        private readonly HashSet<string> _subscribedPairs;
        private readonly SemaphoreSlim _connectionLock;
        private readonly object _stateLock = new();

        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _receiveCts;
        private CancellationTokenSource? _pingCts;
        private Task? _receiveTask;
        private Task? _pingTask;
        private Task? _restPollingTask;
        private DateTimeOffset _lastUpdateTime;
        private long _subscriptionRequestId;
        private int _reconnectAttempts;
        private bool _disposed;

        private WebSocketConnectionState _connectionState = WebSocketConnectionState.Disconnected;

        /// <inheritdoc />
        public event Action<IReadOnlyCollection<ITicker>>? TickersUpdated;

        /// <inheritdoc />
        public event Action<WebSocketConnectionState>? ConnectionStateChanged;

        /// <inheritdoc />
        public WebSocketConnectionState ConnectionState
        {
            get { lock (_stateLock) return _connectionState; }
            private set
            {
                bool changed;
                lock (_stateLock)
                {
                    changed = _connectionState != value;
                    _connectionState = value;
                }
                if (changed)
                {
                    ConnectionStateChanged?.Invoke(value);
                }
            }
        }

        /// <inheritdoc />
        public TimeSpan TimeSinceLastUpdate => DateTimeOffset.Now - _lastUpdateTime;

        /// <inheritdoc />
        public bool IsConnected => ConnectionState == WebSocketConnectionState.Connected;

        /// <inheritdoc />
        public bool IsRestFallbackActive => ConnectionState == WebSocketConnectionState.FallbackToRest;

        public BinanceWebSocketService(ILoggingService loggingService, IHealthCheckService healthCheckService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(RestApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(Constants.Timeouts.HttpRequestTimeoutSeconds)
            };

            _tickers = new ConcurrentDictionary<string, Ticker>();
            _subscribedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _connectionLock = new SemaphoreSlim(1, 1);
            _lastUpdateTime = DateTimeOffset.MinValue;
        }

        /// <inheritdoc />
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    _loggingService.Debug("WebSocket already connected");
                    return;
                }

                await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task ConnectInternalAsync(CancellationToken cancellationToken)
        {
            ConnectionState = WebSocketConnectionState.Connecting;
            _reconnectAttempts = 0;

            try
            {
                _loggingService.Info("Connecting to Binance WebSocket stream...");

                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(PingIntervalSeconds);

                // Connect to combined stream endpoint for multiple subscriptions
                var uri = new Uri($"{CombinedStreamUrl}?streams=!ticker@arr");
                await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                if (_webSocket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException($"WebSocket failed to connect. State: {_webSocket.State}");
                }

                _loggingService.Info("Connected to Binance WebSocket stream");

                // Start background tasks
                _receiveCts = new CancellationTokenSource();
                _pingCts = new CancellationTokenSource();

                _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
                _pingTask = PingLoopAsync(_pingCts.Token);

                ConnectionState = WebSocketConnectionState.Connected;
                _lastUpdateTime = DateTimeOffset.Now;
                _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, "WebSocket connected");
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to connect to Binance WebSocket", ex);
                ConnectionState = WebSocketConnectionState.Disconnected;
                await StartRestFallbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DisconnectInternalAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task DisconnectInternalAsync(CancellationToken cancellationToken)
        {
            if (_disposed || ConnectionState == WebSocketConnectionState.Disconnected)
            {
                return;
            }

            ConnectionState = WebSocketConnectionState.Disconnecting;
            _loggingService.Info("Disconnecting from Binance WebSocket stream...");

            // Cancel background tasks
            _receiveCts?.Cancel();
            _pingCts?.Cancel();

            // Stop REST polling if active
            await StopRestFallbackAsync().ConfigureAwait(false);

            // Close WebSocket gracefully
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Constants.Timeouts.SocketDisconnectTimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", linkedCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _loggingService.Debug("Error during WebSocket close", ex);
                }
            }

            // Wait for tasks to complete
            try
            {
                var tasks = new List<Task>();
                if (_receiveTask != null) tasks.Add(_receiveTask);
                if (_pingTask != null) tasks.Add(_pingTask);

                if (tasks.Count > 0)
                {
                    await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _loggingService.Debug("Error waiting for background tasks", ex);
            }

            // Cleanup
            _webSocket?.Dispose();
            _webSocket = null;
            _receiveCts?.Dispose();
            _receiveCts = null;
            _pingCts?.Dispose();
            _pingCts = null;

            ConnectionState = WebSocketConnectionState.Disconnected;
            _loggingService.Info("Disconnected from Binance WebSocket stream");
        }

        /// <inheritdoc />
        public async Task SubscribeToTickersAsync(IEnumerable<string> pairs, CancellationToken cancellationToken = default)
        {
            var pairsList = pairs.ToList();
            if (pairsList.Count == 0) return;

            foreach (var pair in pairsList)
            {
                _subscribedPairs.Add(pair.ToUpperInvariant());
            }

            if (!IsConnected || _webSocket == null)
            {
                _loggingService.Debug($"WebSocket not connected, pairs saved for subscription: {string.Join(", ", pairsList)}");
                return;
            }

            var streams = pairsList.Select(p => $"{p.ToLowerInvariant()}@ticker").ToArray();
            var request = new BinanceSubscriptionRequest
            {
                Method = "SUBSCRIBE",
                Params = streams,
                Id = Interlocked.Increment(ref _subscriptionRequestId)
            };

            await SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
            _loggingService.Debug($"Subscribed to ticker streams: {string.Join(", ", streams)}");
        }

        /// <inheritdoc />
        public async Task UnsubscribeFromTickersAsync(IEnumerable<string> pairs, CancellationToken cancellationToken = default)
        {
            var pairsList = pairs.ToList();
            if (pairsList.Count == 0) return;

            foreach (var pair in pairsList)
            {
                _subscribedPairs.Remove(pair.ToUpperInvariant());
            }

            if (!IsConnected || _webSocket == null)
            {
                return;
            }

            var streams = pairsList.Select(p => $"{p.ToLowerInvariant()}@ticker").ToArray();
            var request = new BinanceSubscriptionRequest
            {
                Method = "UNSUBSCRIBE",
                Params = streams,
                Id = Interlocked.Increment(ref _subscriptionRequestId)
            };

            await SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
            _loggingService.Debug($"Unsubscribed from ticker streams: {string.Join(", ", streams)}");
        }

        /// <inheritdoc />
        public async Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ConnectionState = WebSocketConnectionState.Reconnecting;
                _loggingService.Info("Reconnecting to Binance WebSocket...");

                await DisconnectInternalAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), cancellationToken).ConfigureAwait(false);
                await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);

                // Resubscribe to any previously subscribed pairs
                if (_subscribedPairs.Count > 0)
                {
                    await SubscribeToTickersAsync(_subscribedPairs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to reconnect to Binance WebSocket", ex);
                await HandleReconnectFailureAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<ITicker>> FetchTickersViaRestAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _loggingService.Debug("Fetching tickers via REST API...");

                var response = await _httpClient.GetAsync("/ticker/24hr", cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var restTickers = JsonSerializer.Deserialize<List<BinanceRestTicker>>(content);

                if (restTickers == null)
                {
                    return Array.Empty<ITicker>();
                }

                var tickers = new List<Ticker>();
                foreach (var restTicker in restTickers)
                {
                    if (decimal.TryParse(restTicker.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var lastPrice) &&
                        decimal.TryParse(restTicker.BidPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var bidPrice) &&
                        decimal.TryParse(restTicker.AskPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var askPrice))
                    {
                        var ticker = new Ticker
                        {
                            Pair = restTicker.Symbol,
                            LastPrice = lastPrice,
                            BidPrice = bidPrice,
                            AskPrice = askPrice
                        };

                        _tickers.AddOrUpdate(restTicker.Symbol, ticker, (_, _) => ticker);
                        tickers.Add(ticker);
                    }
                }

                _lastUpdateTime = DateTimeOffset.Now;
                _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, $"REST: {tickers.Count} tickers");

                _loggingService.Debug($"Fetched {tickers.Count} tickers via REST API");
                return tickers;
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to fetch tickers via REST API", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets all cached tickers.
        /// </summary>
        public IReadOnlyDictionary<string, Ticker> GetTickers() => _tickers;

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            var messageBuffer = new List<byte>();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        var segment = new ArraySegment<byte>(buffer);
                        var result = await _webSocket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _loggingService.Info($"WebSocket close received: {result.CloseStatus} - {result.CloseStatusDescription}");
                            break;
                        }

                        messageBuffer.AddRange(buffer.Take(result.Count));

                        if (messageBuffer.Count > MaxMessageSize)
                        {
                            _loggingService.Warning("WebSocket message too large, discarding");
                            messageBuffer.Clear();
                            continue;
                        }

                        if (result.EndOfMessage)
                        {
                            var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                            messageBuffer.Clear();
                            ProcessMessage(message);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (WebSocketException ex)
                    {
                        _loggingService.Error("WebSocket receive error", ex);
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // If we exited the loop unexpectedly, trigger reconnect
            if (!cancellationToken.IsCancellationRequested)
            {
                _ = HandleConnectionLostAsync();
            }
        }

        private void ProcessMessage(string message)
        {
            try
            {
                // Check if it's a combined stream message
                if (message.Contains("\"stream\""))
                {
                    ProcessCombinedStreamMessage(message);
                }
                // Check if it's an array of ticker updates
                else if (message.StartsWith("["))
                {
                    ProcessTickerArrayMessage(message);
                }
                // Check if it's a subscription response
                else if (message.Contains("\"result\""))
                {
                    ProcessSubscriptionResponse(message);
                }
                else
                {
                    _loggingService.Debug($"Unhandled WebSocket message type");
                }
            }
            catch (JsonException ex)
            {
                _loggingService.Debug($"Failed to parse WebSocket message: {ex.Message}");
            }
        }

        private void ProcessCombinedStreamMessage(string message)
        {
            // Try to parse as combined stream with ticker array
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("stream", out var streamProp) &&
                    streamProp.GetString()?.EndsWith("@arr") == true &&
                    root.TryGetProperty("data", out var dataProp))
                {
                    // It's an array stream (like !ticker@arr)
                    var tickerMessages = JsonSerializer.Deserialize<List<BinanceTickerMessage>>(dataProp.GetRawText());
                    if (tickerMessages != null)
                    {
                        ProcessTickerUpdates(tickerMessages);
                    }
                }
                else if (root.TryGetProperty("data", out var singleData))
                {
                    // It's a single ticker stream
                    var tickerMessage = JsonSerializer.Deserialize<BinanceTickerMessage>(singleData.GetRawText());
                    if (tickerMessage != null)
                    {
                        ProcessTickerUpdates(new List<BinanceTickerMessage> { tickerMessage });
                    }
                }
            }
            catch (JsonException ex)
            {
                _loggingService.Debug($"Failed to parse combined stream message: {ex.Message}");
            }
        }

        private void ProcessTickerArrayMessage(string message)
        {
            var tickerMessages = JsonSerializer.Deserialize<List<BinanceTickerMessage>>(message);
            if (tickerMessages != null)
            {
                ProcessTickerUpdates(tickerMessages);
            }
        }

        private void ProcessTickerUpdates(List<BinanceTickerMessage> tickerMessages)
        {
            var updatedTickers = new List<ITicker>();

            foreach (var tm in tickerMessages)
            {
                if (string.IsNullOrEmpty(tm.Symbol)) continue;

                if (decimal.TryParse(tm.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var lastPrice) &&
                    decimal.TryParse(tm.BidPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var bidPrice) &&
                    decimal.TryParse(tm.AskPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var askPrice))
                {
                    var ticker = _tickers.AddOrUpdate(
                        tm.Symbol,
                        key => new Ticker { Pair = key, LastPrice = lastPrice, BidPrice = bidPrice, AskPrice = askPrice },
                        (key, existing) =>
                        {
                            existing.LastPrice = lastPrice;
                            existing.BidPrice = bidPrice;
                            existing.AskPrice = askPrice;
                            return existing;
                        });

                    updatedTickers.Add(ticker);
                }
            }

            if (updatedTickers.Count > 0)
            {
                _lastUpdateTime = DateTimeOffset.Now;
                _healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, $"WS: {updatedTickers.Count} updates");
                TickersUpdated?.Invoke(updatedTickers);
            }
        }

        private void ProcessSubscriptionResponse(string message)
        {
            var response = JsonSerializer.Deserialize<BinanceSubscriptionResponse>(message);
            if (response != null)
            {
                _loggingService.Debug($"Subscription response received for request {response.Id}: {(response.Result == null ? "success" : response.Result)}");
            }
        }

        private async Task PingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), cancellationToken).ConfigureAwait(false);

                    if (_webSocket?.State == WebSocketState.Open)
                    {
                        // Send a pong frame (Binance expects client to respond to server ping)
                        // Also send a ping to verify connection is alive
                        var pingMessage = Encoding.UTF8.GetBytes("ping");
                        await _webSocket.SendAsync(new ArraySegment<byte>(pingMessage), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                        _loggingService.Debug("WebSocket ping sent");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _loggingService.Debug($"Ping error: {ex.Message}");
                }
            }
        }

        private async Task SendMessageAsync<T>(T message, CancellationToken cancellationToken)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleConnectionLostAsync()
        {
            if (_disposed) return;

            _loggingService.Warning("WebSocket connection lost, attempting reconnect...");

            // Ensure we don't hold the connection lock during reconnect
            _ = Task.Run(async () =>
            {
                while (_reconnectAttempts < MaxReconnectAttempts && !_disposed)
                {
                    _reconnectAttempts++;
                    _loggingService.Info($"Reconnection attempt {_reconnectAttempts}/{MaxReconnectAttempts}");

                    try
                    {
                        await ReconnectAsync(CancellationToken.None).ConfigureAwait(false);
                        _loggingService.Info("WebSocket reconnected successfully");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _loggingService.Warning($"Reconnection attempt failed: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds * _reconnectAttempts)).ConfigureAwait(false);
                    }
                }

                // Max reconnect attempts reached, switch to REST fallback
                await HandleReconnectFailureAsync(CancellationToken.None).ConfigureAwait(false);
            });
        }

        private async Task HandleReconnectFailureAsync(CancellationToken cancellationToken)
        {
            _loggingService.Warning("Max reconnection attempts reached, switching to REST fallback");
            await StartRestFallbackAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task StartRestFallbackAsync(CancellationToken cancellationToken)
        {
            if (IsRestFallbackActive) return;

            ConnectionState = WebSocketConnectionState.FallbackToRest;
            _loggingService.Info("Starting REST API fallback mode");

            // Start REST polling
            _restPollingTask = RestPollingLoopAsync(cancellationToken);

            // Try to reconnect WebSocket in background
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                _reconnectAttempts = 0;
                await HandleConnectionLostAsync().ConfigureAwait(false);
            }, cancellationToken);
        }

        private async Task StopRestFallbackAsync()
        {
            if (_restPollingTask != null)
            {
                // REST polling will stop on its own when state changes
                try
                {
                    await Task.WhenAny(_restPollingTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _loggingService.Debug($"Error stopping REST polling: {ex.Message}");
                }
                _restPollingTask = null;
            }
        }

        private async Task RestPollingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsRestFallbackActive)
            {
                try
                {
                    var tickers = await FetchTickersViaRestAsync(cancellationToken).ConfigureAwait(false);
                    TickersUpdated?.Invoke(tickers);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"REST polling error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _ = DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            _httpClient.Dispose();
            _connectionLock.Dispose();
        }
    }

    /// <summary>
    /// REST API ticker response model.
    /// </summary>
    internal class BinanceRestTicker
    {
        [System.Text.Json.Serialization.JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("lastPrice")]
        public string LastPrice { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("bidPrice")]
        public string BidPrice { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("askPrice")]
        public string AskPrice { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("priceChange")]
        public string PriceChange { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("priceChangePercent")]
        public string PriceChangePercent { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("volume")]
        public string Volume { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("quoteVolume")]
        public string QuoteVolume { get; set; } = string.Empty;
    }
}
