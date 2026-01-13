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
    }
}
