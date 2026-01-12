using IntelliTrader.Core;
using IntelliTrader.Signals.Base;
using MessagePack;

namespace IntelliTrader.Backtesting
{
    [MessagePackObject]
    public class SignalData : ISignal
    {
        [Key(0)]
        public virtual string? Name { get; set; }
        [Key(1)]
        public virtual string? Pair { get; set; }
        [Key(2)]
        public virtual long? Volume { get; set; }
        [Key(3)]
        public virtual double? VolumeChange { get; set; }
        [Key(4)]
        public virtual decimal? Price { get; set; }
        [Key(5)]
        public virtual decimal? PriceChange { get; set; }
        [Key(6)]
        public virtual double? Rating { get; set; }
        [Key(7)]
        public virtual double? RatingChange { get; set; }
        [Key(8)]
        public virtual double? Volatility { get; set; }

        public ISignal ToSignal()
        {
            return new Signal
            {
                Name = Name,
                Pair = Pair,
                Volume = Volume,
                VolumeChange = VolumeChange,
                Price = Price,
                PriceChange = PriceChange,
                Rating = Rating,
                RatingChange = RatingChange,
                Volatility = Volatility
            };
        }

        public static SignalData FromSignal(ISignal signal)
        {
            return new SignalData
            {
                Name = signal.Name,
                Pair = signal.Pair,
                Volume = signal.Volume,
                VolumeChange = signal.VolumeChange,
                Price = signal.Price,
                PriceChange = signal.PriceChange,
                Rating = signal.Rating,
                RatingChange = signal.RatingChange,
                Volatility = signal.Volatility
            };
        }
    }
}
