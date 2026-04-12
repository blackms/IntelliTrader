namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for exchange resilience policies including retry, circuit breaker,
    /// rate limiting, and WebSocket reconnection settings.
    /// </summary>
    public interface IResilienceConfig
    {
        // === Read Operations ===

        /// <summary>
        /// Timeout for read operations in seconds.
        /// </summary>
        int ReadTimeoutSeconds { get; }

        /// <summary>
        /// Maximum concurrent read operations allowed.
        /// </summary>
        int MaxConcurrentReads { get; }

        /// <summary>
        /// Maximum queued read operations when concurrency limit is reached.
        /// </summary>
        int ReadQueueLimit { get; }

        /// <summary>
        /// Maximum retry attempts for read operations.
        /// </summary>
        int ReadMaxRetryAttempts { get; }

        /// <summary>
        /// Initial delay before first retry for read operations (milliseconds).
        /// </summary>
        int ReadInitialDelayMs { get; }

        // === Order Operations ===

        /// <summary>
        /// Timeout for order operations in seconds.
        /// </summary>
        int OrderTimeoutSeconds { get; }

        /// <summary>
        /// Maximum concurrent order operations allowed.
        /// </summary>
        int MaxConcurrentOrders { get; }

        /// <summary>
        /// Maximum queued order operations when concurrency limit is reached.
        /// </summary>
        int OrderQueueLimit { get; }

        /// <summary>
        /// Maximum retry attempts for order operations.
        /// Set to 0 to prevent duplicate orders.
        /// </summary>
        int OrderMaxRetryAttempts { get; }

        /// <summary>
        /// Initial delay before first retry for order operations (milliseconds).
        /// </summary>
        int OrderInitialDelayMs { get; }

        // === Circuit Breaker (Reads) ===

        /// <summary>
        /// Failure ratio threshold to open circuit breaker for reads (0.0-1.0).
        /// </summary>
        double CircuitBreakerFailureRatio { get; }

        /// <summary>
        /// Sampling duration for circuit breaker failure ratio calculation (seconds).
        /// </summary>
        int CircuitBreakerSamplingSeconds { get; }

        /// <summary>
        /// Minimum number of operations before circuit breaker can open.
        /// </summary>
        int CircuitBreakerMinimumThroughput { get; }

        /// <summary>
        /// Duration the circuit breaker stays open before transitioning to half-open (seconds).
        /// </summary>
        int CircuitBreakerBreakDurationSeconds { get; }

        // === Circuit Breaker (Orders) ===

        /// <summary>
        /// Failure ratio threshold to open circuit breaker for orders (0.0-1.0).
        /// </summary>
        double OrderCircuitBreakerFailureRatio { get; }

        /// <summary>
        /// Sampling duration for order circuit breaker (seconds).
        /// </summary>
        int OrderCircuitBreakerSamplingSeconds { get; }

        /// <summary>
        /// Minimum number of orders before circuit breaker can open.
        /// </summary>
        int OrderCircuitBreakerMinimumThroughput { get; }

        /// <summary>
        /// Duration the order circuit breaker stays open (seconds).
        /// </summary>
        int OrderCircuitBreakerBreakDurationSeconds { get; }

        // === WebSocket ===

        /// <summary>
        /// Timeout for WebSocket connection establishment (seconds).
        /// </summary>
        int WebSocketConnectTimeoutSeconds { get; }

        /// <summary>
        /// Maximum reconnection attempts before falling back to REST.
        /// </summary>
        int WebSocketMaxReconnectAttempts { get; }

        /// <summary>
        /// Delay between WebSocket reconnection attempts (seconds).
        /// </summary>
        int WebSocketReconnectDelaySeconds { get; }

        // === Rate Limiting ===

        /// <summary>
        /// Maximum requests per minute.
        /// </summary>
        int RateLimitPermitsPerMinute { get; }

        /// <summary>
        /// Maximum queued requests when rate limit is reached.
        /// </summary>
        int RateLimitQueueLimit { get; }
    }
}
