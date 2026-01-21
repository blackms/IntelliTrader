using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Order execution result status.
    /// Values are aligned with ExchangeSharp.ExchangeAPIOrderResult enum.
    /// </summary>
    public enum OrderResult
    {
        Unknown = 0,
        Filled = 1,
        FilledPartially = 2,
        Pending = 3,
        Error = 4,
        Canceled = 5,
        FilledPartiallyAndCancelled = 6,
        PendingCancel = 7,
        Rejected = 8,
        Expired = 9,
        Open = 10
    }
}
