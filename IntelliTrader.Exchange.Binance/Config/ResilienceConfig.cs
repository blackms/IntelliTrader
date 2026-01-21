namespace IntelliTrader.Exchange.Binance.Config
{
    /// <summary>
    /// Configuration options for exchange resilience policies.
    /// Defines timeout, retry, circuit breaker, and rate limiting settings
    /// for both read operations and order execution.
    /// </summary>
    public class ResilienceConfig
    {
        // === Read Operations ===

        /// <summary>
        /// Timeout for read operations in seconds.
        /// </summary>
        public int ReadTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum concurrent read operations allowed.
        /// </summary>
        public int MaxConcurrentReads { get; set; } = 10;

        /// <summary>
        /// Maximum queued read operations when concurrency limit is reached.
        /// </summary>
        public int ReadQueueLimit { get; set; } = 20;

        /// <summary>
        /// Maximum retry attempts for read operations.
        /// </summary>
        public int ReadMaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Initial delay before first retry for read operations (milliseconds).
        /// </summary>
        public int ReadInitialDelayMs { get; set; } = 1000;

        // === Order Operations ===

        /// <summary>
        /// Timeout for order operations in seconds.
        /// Shorter than reads because markets move fast.
        /// </summary>
        public int OrderTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Maximum concurrent order operations allowed.
        /// Kept low to prevent overwhelming the exchange.
        /// </summary>
        public int MaxConcurrentOrders { get; set; } = 3;

        /// <summary>
        /// Maximum queued order operations when concurrency limit is reached.
        /// </summary>
        public int OrderQueueLimit { get; set; } = 5;

        /// <summary>
        /// Maximum retry attempts for order operations.
        /// CRITICAL: Keep very low (1) to prevent duplicate orders.
        /// Only retries on connection errors, not response errors.
        /// </summary>
        public int OrderMaxRetryAttempts { get; set; } = 1;

        /// <summary>
        /// Initial delay before first retry for order operations (milliseconds).
        /// </summary>
        public int OrderInitialDelayMs { get; set; } = 500;

        // === Circuit Breaker (Reads) ===

        /// <summary>
        /// Failure ratio threshold to open circuit breaker for reads (0.0-1.0).
        /// </summary>
        public double CircuitBreakerFailureRatio { get; set; } = 0.5;

        /// <summary>
        /// Sampling duration for circuit breaker failure ratio calculation (seconds).
        /// </summary>
        public int CircuitBreakerSamplingSeconds { get; set; } = 30;

        /// <summary>
        /// Minimum number of operations before circuit breaker can open.
        /// </summary>
        public int CircuitBreakerMinimumThroughput { get; set; } = 8;

        /// <summary>
        /// Duration the circuit breaker stays open before transitioning to half-open (seconds).
        /// </summary>
        public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

        // === Circuit Breaker (Orders) ===

        /// <summary>
        /// Failure ratio threshold to open circuit breaker for orders (0.0-1.0).
        /// More sensitive than reads to prevent order failures.
        /// </summary>
        public double OrderCircuitBreakerFailureRatio { get; set; } = 0.3;

        /// <summary>
        /// Sampling duration for order circuit breaker (seconds).
        /// </summary>
        public int OrderCircuitBreakerSamplingSeconds { get; set; } = 60;

        /// <summary>
        /// Minimum number of orders before circuit breaker can open.
        /// </summary>
        public int OrderCircuitBreakerMinimumThroughput { get; set; } = 5;

        /// <summary>
        /// Duration the order circuit breaker stays open (seconds).
        /// </summary>
        public int OrderCircuitBreakerBreakDurationSeconds { get; set; } = 60;

        // === WebSocket ===

        /// <summary>
        /// Timeout for WebSocket connection establishment (seconds).
        /// </summary>
        public int WebSocketConnectTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum reconnection attempts before falling back to REST.
        /// </summary>
        public int WebSocketMaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// Delay between WebSocket reconnection attempts (seconds).
        /// </summary>
        public int WebSocketReconnectDelaySeconds { get; set; } = 5;

        // === Rate Limiting ===

        /// <summary>
        /// Maximum requests per minute (below Binance's 1200/min limit).
        /// </summary>
        public int RateLimitPermitsPerMinute { get; set; } = 1000;

        /// <summary>
        /// Maximum queued requests when rate limit is reached.
        /// </summary>
        public int RateLimitQueueLimit { get; set; } = 50;

        /// <summary>
        /// Creates a default configuration with recommended values.
        /// </summary>
        public static ResilienceConfig Default => new();
    }
}
