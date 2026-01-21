using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface ITradingConfig : IBuyConfig, IBuyDCAConfig, ISellConfig, ISellDCAConfig
    {
        bool Enabled { get; }
        string Market { get; }
        string Exchange { get; }
        int MaxPairs { get; }
        decimal MinCost { get; }
        List<string> ExcludedPairs { get; }

        bool RepeatLastDCALevel { get; }
        List<DCALevel> DCALevels { get; }

        double TradingCheckInterval { get; }
        double AccountRefreshInterval { get; }
        decimal AccountInitialBalance { get; }
        DateTimeOffset AccountInitialBalanceDate { get; }
        string AccountFilePath { get; }

        bool VirtualTrading { get; }
        decimal VirtualAccountInitialBalance { get; }
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

        ITradingConfig Clone();

    }
}
