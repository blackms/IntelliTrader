using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Trading
{
    public static class Utils
    {
        public static decimal CalculateMargin(decimal oldValue, decimal newValue)
        {
            // Guard against division by zero (e.g., zero cost basis).
            // Example: CalculateMargin(0, 100) => 0 instead of DivideByZeroException
            if (oldValue == 0m) return 0m;
            return (newValue - oldValue) / oldValue * 100;
        }
    }
}
