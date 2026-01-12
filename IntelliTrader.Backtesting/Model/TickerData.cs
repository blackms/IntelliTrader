using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using MessagePack;

namespace IntelliTrader.Backtesting
{
    [MessagePackObject]
    public class TickerData : ITicker
    {
        [Key(0)]
        public virtual string? Pair { get; set; }
        [Key(1)]
        public virtual decimal BidPrice { get; set; }
        [Key(2)]
        public virtual decimal AskPrice { get; set; }
        [Key(3)]
        public virtual decimal LastPrice { get; set; }

        public ITicker ToTicker()
        {
            return new Ticker
            {
                Pair = Pair,
                BidPrice = BidPrice,
                AskPrice = AskPrice,
                LastPrice = LastPrice
            };
        }

        public static TickerData FromTicker(ITicker ticker)
        {
            return new TickerData
            {
                Pair = ticker.Pair,
                BidPrice = ticker.BidPrice,
                AskPrice = ticker.AskPrice,
                LastPrice = ticker.LastPrice
            };
        }
    }
}
