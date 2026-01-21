using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Trading
{
    internal class TradingRulesConfig
    {
        public RuleProcessingMode ProcessingMode { get; set; }
        public double CheckInterval { get; set; }

        // Buy configuration
        public bool BuyEnabled { get; set; } = true;
        public decimal BuyMinBalance { get; set; }
        public double BuySamePairTimeout { get; set; }
        public decimal BuyTrailing { get; set; }
        public decimal BuyTrailingStopMargin { get; set; }
        public BuyTrailingStopAction BuyTrailingStopAction { get; set; }

        // Sell configuration
        public bool SellEnabled { get; set; } = true;
        public decimal SellMargin { get; set; } = 1;
        public decimal SellTrailing { get; set; }
        public decimal SellTrailingStopMargin { get; set; }
        public SellTrailingStopAction SellTrailingStopAction { get; set; }
        public bool SellStopLossEnabled { get; set; }
        public bool SellStopLossAfterDCA { get; set; }
        public double SellStopLossMinAge { get; set; }
        public decimal SellStopLossMargin { get; set; } = -10;

        // Swap configuration
        public bool SwapEnabled { get; set; }
        public List<string> SwapSignalRules { get; set; }
        public int SwapTimeout { get; set; }
    }
}
