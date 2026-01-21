using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface ISellConfig
    {
        bool SellEnabled { get; set; }
        OrderType SellType { get; }
        decimal SellMargin { get; }
        decimal SellTrailing { get; }
        decimal SellTrailingStopMargin { get; }
        SellTrailingStopAction SellTrailingStopAction { get; }
        bool SellStopLossEnabled { get; }
        bool SellStopLossAfterDCA { get; }
        double SellStopLossMinAge { get; }
        decimal SellStopLossMargin { get; }

        /// <summary>
        /// Configuration for dynamic ATR-based stop-loss.
        /// When StopLoss.Type is ATR, the SellStopLossMargin becomes a fallback minimum.
        /// </summary>
        IStopLossConfig StopLoss { get; }
    }
}
