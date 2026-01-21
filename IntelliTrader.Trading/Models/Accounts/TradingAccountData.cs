using IntelliTrader.Core;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace IntelliTrader.Trading
{
    internal class TradingAccountData
    {
        [DecimalFormatJsonConverterAttribute(8)]
        public decimal Balance { get; set; }
        public ConcurrentDictionary<string, TradingPair>? TradingPairs { get; set; }
    }
}
