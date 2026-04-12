using IntelliTrader.Core;
using IntelliTrader.Exchange.Binance.Config;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Binance.Resilience
{
    /// <summary>
    /// Factory for creating resilience pipelines for exchange operations.
    /// Provides separate pipelines for read operations (aggressive retry) and
    /// order operations (conservative retry to prevent duplicate orders).
    /// </summary>
    public class ExchangeResiliencePipelines
    {
        private readonly ILoggingService _loggingService;
        private readonly ResilienceConfig _config;

        /// <summary>
        /// Pipeline for read operations (GetTickers, GetAvailableAmounts, GetMyTrades).
        /// Uses aggressive retry with exponential backoff and jitter.
        /// </summary>
        public ResiliencePipeline ReadPipeline { get; }

        /// <summary>
        /// Pipeline for order operations (PlaceOrder).
        /// CRITICAL: Uses conservative retry (max 1) to prevent duplicate orders.
        /// </summary>
        public ResiliencePipeline OrderPipeline { get; }

        /// <summary>
        /// Pipeline for WebSocket operations (connect, reconnect).
        /// </summary>
        public ResiliencePipeline WebSocketPipeline { get; }

        /// <summary>
        /// Initializes the resilience pipelines with the specified configuration.
        /// </summary>
        /// <param name="loggingService">Logging service for telemetry.</param>
        /// <param name="config">Resilience configuration. If null, uses defaults.</param>
        public ExchangeResiliencePipelines(ILoggingService loggingService, ResilienceConfig? config = null)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _config = config ?? ResilienceConfig.Default;

            ReadPipeline = CreateReadPipeline();
            OrderPipeline = CreateOrderPipeline();
            WebSocketPipeline = CreateWebSocketPipeline();
        }

        /// <summary>
        /// Creates the read operations pipeline with:
        /// - Timeout (30s default)
        /// - Concurrency limiter (bulkhead)
        /// - Circuit breaker
        /// - Retry with exponential backoff and jitter
        /// </summary>
        private ResiliencePipeline CreateReadPipeline()
        {
            return new ResiliencePipelineBuilder()
                // 1. Timeout - prevents indefinite waits
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(_config.ReadTimeoutSeconds),
                    OnTimeout = args =>
                    {
                        _loggingService.Warning($"[Resilience] Exchange read operation timed out after {args.Timeout.TotalSeconds}s");
                        return default;
                    }
                })

                // 2. Concurrency limiter (bulkhead) - limits concurrent reads
                .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = _config.MaxConcurrentReads,
                    QueueLimit = _config.ReadQueueLimit
                })

                // 3. Circuit breaker - fails fast when service is unhealthy
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = _config.CircuitBreakerFailureRatio,
                    SamplingDuration = TimeSpan.FromSeconds(_config.CircuitBreakerSamplingSeconds),
                    MinimumThroughput = _config.CircuitBreakerMinimumThroughput,
                    BreakDuration = TimeSpan.FromSeconds(_config.CircuitBreakerBreakDurationSeconds),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .Handle<TimeoutRejectedException>()
                        .Handle<Exception>(ex => IsServerError(ex)),
                    OnOpened = args =>
                    {
                        _loggingService.Warning($"[Resilience] Circuit breaker OPENED for exchange reads. Duration: {args.BreakDuration}. Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        _loggingService.Info("[Resilience] Circuit breaker CLOSED for exchange reads - service recovered");
                        return default;
                    },
                    OnHalfOpened = args =>
                    {
                        _loggingService.Info("[Resilience] Circuit breaker HALF-OPEN for exchange reads - testing service");
                        return default;
                    }
                })

                // 4. Retry with exponential backoff and jitter
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = _config.ReadMaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(_config.ReadInitialDelayMs),
                    UseJitter = true, // Adds decorrelated jitter to prevent thundering herd
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                        .Handle<TimeoutException>()
                        .Handle<Exception>(ex => IsTransientError(ex)),
                    OnRetry = args =>
                    {
                        _loggingService.Info($"[Resilience] Retrying exchange read (attempt {args.AttemptNumber + 1}/{_config.ReadMaxRetryAttempts}) after {args.RetryDelay.TotalSeconds:F1}s. Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                        return default;
                    }
                })
                .Build();
        }

        /// <summary>
        /// Creates the order operations pipeline with:
        /// - Timeout (15s - shorter for fast markets)
        /// - Concurrency limiter (strict limit on concurrent orders)
        /// - Circuit breaker (more sensitive for orders)
        /// - CONSERVATIVE retry (max 1 attempt - CRITICAL to prevent duplicate orders)
        /// </summary>
        private ResiliencePipeline CreateOrderPipeline()
        {
            return new ResiliencePipelineBuilder()
                // 1. Concurrency limiter - strict limit on concurrent orders
                .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = _config.MaxConcurrentOrders,
                    QueueLimit = _config.OrderQueueLimit
                })

                // 2. Circuit breaker - more sensitive for orders
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = _config.OrderCircuitBreakerFailureRatio,
                    SamplingDuration = TimeSpan.FromSeconds(_config.OrderCircuitBreakerSamplingSeconds),
                    MinimumThroughput = _config.OrderCircuitBreakerMinimumThroughput,
                    BreakDuration = TimeSpan.FromSeconds(_config.OrderCircuitBreakerBreakDurationSeconds),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(),
                    OnOpened = args =>
                    {
                        _loggingService.Error($"[Resilience] ORDER CIRCUIT BREAKER OPENED! Trading suspended for {args.BreakDuration}. Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        _loggingService.Info("[Resilience] Order circuit breaker CLOSED - trading resumed");
                        return default;
                    },
                    OnHalfOpened = args =>
                    {
                        _loggingService.Info("[Resilience] Order circuit breaker HALF-OPEN - testing order capability");
                        return default;
                    }
                })

                // 3. Retry - VERY CONSERVATIVE for orders to prevent duplicates
                // CRITICAL: Only retry on true connection errors where we know the request
                // never reached the server. Do NOT retry on TaskCanceledException/timeout
                // because the server may have executed the order but the response was slow.
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = _config.OrderMaxRetryAttempts, // Should be 1
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(_config.OrderInitialDelayMs),
                    UseJitter = true,
                    // ONLY retry on connection errors, NOT on timeout or any response
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>(ex => IsConnectionError(ex)),
                    OnRetry = args =>
                    {
                        _loggingService.Warning($"[Resilience] RETRYING ORDER (attempt {args.AttemptNumber + 1}/{_config.OrderMaxRetryAttempts}) - VERIFY ORDER STATUS BEFORE RETRY! Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                        return default;
                    }
                })

                // 4. Timeout - INSIDE retry so each attempt gets its own timeout.
                // If a timeout fires, it will NOT trigger a retry (TaskCanceledException
                // is excluded from retry ShouldHandle), preventing duplicate orders.
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(_config.OrderTimeoutSeconds),
                    OnTimeout = args =>
                    {
                        _loggingService.Warning($"[Resilience] Order operation timed out after {args.Timeout.TotalSeconds}s - CHECK ORDER STATUS MANUALLY!");
                        return default;
                    }
                })
                .Build();
        }

        /// <summary>
        /// Creates the WebSocket operations pipeline with:
        /// - Timeout for connection establishment
        /// - Circuit breaker
        /// - Retry with exponential backoff
        /// </summary>
        private ResiliencePipeline CreateWebSocketPipeline()
        {
            return new ResiliencePipelineBuilder()
                // 1. Timeout for connection establishment
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(_config.WebSocketConnectTimeoutSeconds),
                    OnTimeout = args =>
                    {
                        _loggingService.Warning($"[Resilience] WebSocket connection timed out after {args.Timeout.TotalSeconds}s");
                        return default;
                    }
                })

                // 2. Circuit breaker
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(60),
                    MinimumThroughput = 3,
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<Exception>()
                        .Handle<TimeoutRejectedException>(),
                    OnOpened = args =>
                    {
                        _loggingService.Warning("[Resilience] WebSocket circuit breaker opened - falling back to REST");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        _loggingService.Info("[Resilience] WebSocket circuit breaker closed");
                        return default;
                    }
                })

                // 3. Retry for connection with capped backoff
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = _config.WebSocketMaxReconnectAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(_config.WebSocketReconnectDelaySeconds),
                    MaxDelay = TimeSpan.FromMinutes(2), // Cap the backoff
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<Exception>()
                        .Handle<TimeoutRejectedException>(),
                    OnRetry = args =>
                    {
                        _loggingService.Info($"[Resilience] WebSocket reconnecting (attempt {args.AttemptNumber + 1}/{_config.WebSocketMaxReconnectAttempts}) in {args.RetryDelay.TotalSeconds:F0}s");
                        return default;
                    }
                })
                .Build();
        }

        /// <summary>
        /// Determines if an exception indicates a server-side error (5xx or 429).
        /// Uses HttpRequestException.StatusCode when available, falls back to message matching.
        /// </summary>
        private static bool IsServerError(Exception ex)
        {
            if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                return code == 429 || (code >= 500 && code <= 504);
            }

            var message = ex.Message;
            return message.Contains("500") ||
                   message.Contains("502") ||
                   message.Contains("503") ||
                   message.Contains("504") ||
                   message.Contains("429");
        }

        /// <summary>
        /// Determines if an exception is a transient error that should be retried.
        /// Uses HttpRequestException.StatusCode when available, falls back to message matching.
        /// </summary>
        private static bool IsTransientError(Exception ex)
        {
            if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
            }

            var message = ex.Message;
            return message.Contains("429") ||    // Rate limit
                   message.Contains("503") ||    // Service unavailable
                   message.Contains("502") ||    // Bad gateway
                   message.Contains("500") ||    // Internal server error
                   message.Contains("504") ||    // Gateway timeout
                   message.Contains("ECONNRESET") ||
                   message.Contains("ETIMEDOUT") ||
                   message.Contains("connection");
        }

        /// <summary>
        /// Determines if an HTTP exception is a true connection error.
        /// ONLY these should trigger order retries.
        /// </summary>
        private static bool IsConnectionError(HttpRequestException ex)
        {
            // Only retry on true connection failures, not on HTTP responses
            if (ex.InnerException is SocketException)
            {
                return true;
            }

            var message = ex.Message.ToLowerInvariant();
            return message.Contains("econnreset") ||
                   message.Contains("etimedout") ||
                   message.Contains("connection refused") ||
                   message.Contains("connection reset") ||
                   message.Contains("unable to connect") ||
                   message.Contains("network is unreachable");
        }
    }
}
