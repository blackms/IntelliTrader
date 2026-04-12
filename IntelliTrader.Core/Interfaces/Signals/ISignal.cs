using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents a trading signal containing price, volume, rating, and volatility data for a pair.
    /// </summary>
    public interface ISignal
    {
        /// <summary>
        /// The signal source name (e.g., "TradingView-5m").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The trading pair this signal applies to (e.g., "BTCUSDT").
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// The 24-hour trading volume.
        /// </summary>
        long? Volume { get; }

        /// <summary>
        /// The percentage change in volume.
        /// </summary>
        double? VolumeChange { get; set; }

        /// <summary>
        /// The current price from the signal source.
        /// </summary>
        decimal? Price { get; }

        /// <summary>
        /// The percentage change in price.
        /// </summary>
        decimal? PriceChange { get; }

        /// <summary>
        /// The signal rating (e.g., TradingView recommendation score).
        /// </summary>
        double? Rating { get; }

        /// <summary>
        /// The change in signal rating since the last update.
        /// </summary>
        double? RatingChange { get; }

        /// <summary>
        /// The price volatility measurement.
        /// </summary>
        double? Volatility { get; }
    }
}
