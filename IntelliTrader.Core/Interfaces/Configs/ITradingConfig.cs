using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Master trading configuration combining buy, sell, and DCA settings with exchange and account parameters.
    /// </summary>
    public interface ITradingConfig : IBuyConfig, IBuyDCAConfig, ISellConfig, ISellDCAConfig
    {
        /// <summary>
        /// Whether trading is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// The market to trade on (e.g., "BTC", "USDT").
        /// </summary>
        string Market { get; }

        /// <summary>
        /// The exchange identifier (e.g., "Binance").
        /// </summary>
        string Exchange { get; }

        /// <summary>
        /// Maximum number of concurrent trading pairs allowed.
        /// </summary>
        int MaxPairs { get; }

        /// <summary>
        /// Minimum order cost in market currency (orders below this are rejected by the exchange).
        /// </summary>
        decimal MinCost { get; }

        /// <summary>
        /// Trading pairs excluded from signal evaluation and trading.
        /// </summary>
        List<string> ExcludedPairs { get; }

        /// <summary>
        /// Whether to repeat the last DCA level configuration for additional DCA levels beyond those defined.
        /// </summary>
        bool RepeatLastDCALevel { get; }

        /// <summary>
        /// The configured DCA levels with their margin thresholds and multipliers.
        /// </summary>
        List<DCALevel> DCALevels { get; }

        /// <summary>
        /// Interval in seconds between trading rule evaluations.
        /// </summary>
        double TradingCheckInterval { get; }

        /// <summary>
        /// Interval in seconds between account balance refreshes.
        /// </summary>
        double AccountRefreshInterval { get; }

        /// <summary>
        /// The initial account balance for profit tracking.
        /// </summary>
        decimal AccountInitialBalance { get; }

        /// <summary>
        /// The date of the initial account balance for profit tracking.
        /// </summary>
        DateTimeOffset AccountInitialBalanceDate { get; }

        /// <summary>
        /// File path for persisting account state (positions and orders).
        /// </summary>
        string AccountFilePath { get; }

        /// <summary>
        /// Whether to use virtual (paper) trading instead of live trading.
        /// </summary>
        bool VirtualTrading { get; }

        /// <summary>
        /// Initial balance for the virtual trading account.
        /// </summary>
        decimal VirtualAccountInitialBalance { get; }

        /// <summary>
        /// File path for persisting virtual account state.
        /// </summary>
        string VirtualAccountFilePath { get; }

        /// <summary>
        /// Maximum number of orders to keep in history. Oldest orders are removed when this limit is exceeded.
        /// Default is 10,000. Valid range is 100 to 100,000.
        /// </summary>
        int MaxOrderHistorySize { get; }

        /// <summary>
        /// Portfolio risk management configuration.
        /// </summary>
        IRiskManagementConfig RiskManagement { get; }

        /// <summary>
        /// Position sizing configuration for Kelly Criterion and percentage-based sizing.
        /// </summary>
        PositionSizingConfig PositionSizing { get; }

        /// <summary>
        /// Creates a deep copy of this trading configuration.
        /// </summary>
        /// <returns>A new instance with identical settings.</returns>
        ITradingConfig Clone();

    }
}
