using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public static class Constants
    {
        public static class ServiceNames
        {
            public const string CoreService = "Core";
            public const string CachingService = "Caching";
            public const string LoggingService = "Logging";
            public const string TradingService = "Trading";
            public const string ExchangeService = "Exchange";
            public const string IntegrationService = "Integration";
            public const string SignalsService = "Signals";
            public const string RulesService = "Rules";
            public const string NotificationService = "Notification";
            public const string WebService = "Web";
            public const string BacktestingService = "Backtesting";
            public const string BacktestingExchangeService = "BacktestingExchange";
            public const string BacktestingSignalsService = "BacktestingSignals";
        }

        public static class Caching
        {
            public const string SharedCacheFileExtension = "cache";
        }

        public static class HealthChecks
        {
            public const string AccountRefreshed = "Account refreshed";
            public const string TickersUpdated = "Tickers updated";
            public const string TradingPairsProcessed = "Trading pairs processed";
            public const string TradingViewCryptoSignalsReceived = "TV Signals received";
            public const string SignalRulesProcessed = "Signals rules processed";
            public const string TradingRulesProcessed = "Trading rules processed";
            public const string BacktestingSignalsSnapshotTaken = "Backtesting signals snapshot taken";
            public const string BacktestingTickersSnapshotTaken = "Backtesting tickers snapshot taken";
            public const string BacktestingSignalsSnapshotLoaded = "Backtesting signals snapshot loaded";
            public const string BacktestingTickersSnapshotLoaded = "Backtesting tickers snapshot loaded";
        }

        public static class SignalRuleActions
        {
            public const string Swap = "Swap";
        }

        public static class SnapshotEntities
        {
            public const string Signals = "signals";
            public const string Tickers = "tickers";
        }

        public static class TimedTasks
        {
            public const int StandardDelay = 3000;
            public const int DefaultIntervalMs = 1000;
        }

        public static class Timeouts
        {
            /// <summary>Startup delay before starting all timed tasks (ms)</summary>
            public const int StartupDelayMs = 3000;

            /// <summary>Socket disconnect timeout (seconds)</summary>
            public const int SocketDisconnectTimeoutSeconds = 10;

            /// <summary>Service restart timeout (seconds)</summary>
            public const int RestartTimeoutSeconds = 20;

            /// <summary>Default HTTP request timeout (seconds)</summary>
            public const int HttpRequestTimeoutSeconds = 30;
        }

        public static class RetryPolicy
        {
            /// <summary>Maximum number of retry attempts for exchange operations</summary>
            public const int MaxRetryAttempts = 3;

            /// <summary>Initial delay before first retry (ms)</summary>
            public const int InitialRetryDelayMs = 1000;
        }

        public static class Trading
        {
            /// <summary>Default maximum number of orders to keep in history before oldest are removed</summary>
            public const int DefaultMaxOrderHistorySize = 10000;

            /// <summary>Minimum allowed value for MaxOrderHistorySize configuration</summary>
            public const int MinOrderHistorySize = 100;

            /// <summary>Maximum allowed value for MaxOrderHistorySize configuration</summary>
            public const int MaxOrderHistorySize = 100000;
        }

        public static class WebSocket
        {
            /// <summary>Binance WebSocket stream base URL</summary>
            public const string BinanceStreamUrl = "wss://stream.binance.com:9443/ws";

            /// <summary>Binance combined stream URL</summary>
            public const string BinanceCombinedStreamUrl = "wss://stream.binance.com:9443/stream";

            /// <summary>Ping interval in seconds (per Binance 2025 requirements)</summary>
            public const int PingIntervalSeconds = 20;

            /// <summary>Reconnection delay in seconds</summary>
            public const int ReconnectDelaySeconds = 5;

            /// <summary>Maximum reconnection attempts before falling back to REST</summary>
            public const int MaxReconnectAttempts = 5;

            /// <summary>Maximum age of ticker data before forcing reconnection (seconds)</summary>
            public const int MaxTickersAgeSeconds = 60;

            /// <summary>WebSocket receive buffer size in bytes</summary>
            public const int ReceiveBufferSize = 8192;

            /// <summary>Maximum WebSocket message size in bytes (1MB)</summary>
            public const int MaxMessageSize = 1024 * 1024;
        }

        /// <summary>
        /// Resilience policy defaults for exchange operations.
        /// These values align with the POLLY_RESILIENCE_DESIGN.md document.
        /// </summary>
        public static class Resilience
        {
            // === Read Operations ===

            /// <summary>Default timeout for read operations (seconds)</summary>
            public const int DefaultReadTimeoutSeconds = 30;

            /// <summary>Maximum concurrent read operations</summary>
            public const int DefaultMaxConcurrentReads = 10;

            /// <summary>Maximum retry attempts for read operations</summary>
            public const int DefaultReadMaxRetryAttempts = 3;

            /// <summary>Initial delay before first retry for reads (ms)</summary>
            public const int DefaultReadInitialDelayMs = 1000;

            // === Order Operations ===

            /// <summary>Default timeout for order operations (seconds)</summary>
            public const int DefaultOrderTimeoutSeconds = 15;

            /// <summary>Maximum concurrent order operations</summary>
            public const int DefaultMaxConcurrentOrders = 3;

            /// <summary>
            /// Maximum retry attempts for order operations.
            /// CRITICAL: Keep at 1 to prevent duplicate orders.
            /// </summary>
            public const int DefaultOrderMaxRetryAttempts = 1;

            /// <summary>Initial delay before first retry for orders (ms)</summary>
            public const int DefaultOrderInitialDelayMs = 500;

            // === Circuit Breaker ===

            /// <summary>Failure ratio to trip circuit breaker for reads (0.0-1.0)</summary>
            public const double DefaultCircuitBreakerFailureRatio = 0.5;

            /// <summary>Sampling duration for circuit breaker (seconds)</summary>
            public const int DefaultCircuitBreakerSamplingSeconds = 30;

            /// <summary>Minimum operations before circuit breaker can trip</summary>
            public const int DefaultCircuitBreakerMinimumThroughput = 8;

            /// <summary>Duration circuit breaker stays open (seconds)</summary>
            public const int DefaultCircuitBreakerBreakDurationSeconds = 30;

            /// <summary>Failure ratio to trip circuit breaker for orders (more sensitive)</summary>
            public const double DefaultOrderCircuitBreakerFailureRatio = 0.3;

            // === Rate Limiting ===

            /// <summary>
            /// Maximum requests per minute (below Binance's 1200/min limit).
            /// Provides 17% buffer to avoid rate limit violations.
            /// </summary>
            public const int DefaultRateLimitPermitsPerMinute = 1000;

            /// <summary>Maximum queued requests when rate limit is reached</summary>
            public const int DefaultRateLimitQueueLimit = 50;
        }
    }
}
